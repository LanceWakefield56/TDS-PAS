using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Messaging;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Scribe;

namespace BrightPAS.Modules.Tests;

/// <summary>0.18.0 offline import: files from USB sticks and shares are split into the messages a live connection would have delivered, safely.</summary>
public class OfflineImportTests
{
    private const string Oru1 = "MSH|^~\\&|LAB|X|BrightPAS|TDS|202611101030||ORU^R01|C1|P|2.4\rPID|1||ZZZ0016^^^NHI^NHI||TEST^A\rOBX|1|NM|718-7^Hb||132|g/L\r";
    private const string Oru2 = "MSH|^~\\&|LAB|X|BrightPAS|TDS|202611101031||ORU^R01|C2|P|2.4\rPID|1||ZZZ0024^^^NHI^NHI||TEST^B\rOBX|1|NM|718-7^Hb||140|g/L\r";

    [Fact]
    public void Hl7_files_split_into_messages_whatever_the_framing_line_ends_or_batch_envelope()
    {
        // Plain file with Windows line ends and a byte-order mark.
        var crlf = "﻿" + (Oru1 + Oru2).Replace("\r", "\r\n");
        var a = MessageFile.SplitHl7(crlf);
        Assert.Equal(2, a.Count);
        Assert.StartsWith("MSH|", a[0], StringComparison.Ordinal);
        Assert.DoesNotContain('\n', a[0]);
        Assert.EndsWith("\r", a[1], StringComparison.Ordinal);
        // MLLP-framed capture (as a protocol analyser or device log saves it).
        var mllp = $"\v{Oru1}\x1c\r\v{Oru2}\x1c\r";
        Assert.Equal(a, MessageFile.SplitHl7(mllp));
        // HL7 batch file with FHS/BHS envelopes and a stray note before the first message.
        var batch = "exported by lab\nFHS|^~\\&|LAB\rBHS|^~\\&|LAB\r" + Oru1 + Oru2 + "BTS|2\rFTS|1\r";
        Assert.Equal(a, MessageFile.SplitHl7(batch));
        Assert.Empty(MessageFile.SplitHl7("this is not HL7"));
    }

    [Fact]
    public void Zip_from_a_share_is_opened_with_limits_and_index_files_are_skipped()
    {
        byte[] Zip(params (string Name, byte[] Data)[] files)
        {
            using var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var (n, d) in files) { using var e = z.CreateEntry(n).Open(); e.Write(d); }
            return ms.ToArray();
        }
        var zip = Zip(("export/one.hl7", Encoding.UTF8.GetBytes(Oru1)), ("export/two.hl7", Encoding.UTF8.GetBytes(Oru2)), ("DICOMDIR", [1, 2, 3]), (".DS_Store", [1]));
        var items = MessageFile.Expand("usb.zip", zip);
        Assert.Equal(2, items.Count);
        Assert.Equal("usb.zip › export/one.hl7", items[0].Name);
        Assert.Single(MessageFile.Expand("plain.hl7", Encoding.UTF8.GetBytes(Oru1)));
        // Too many entries is refused rather than processed partly.
        var many = Zip(Enumerable.Range(0, MessageFile.MaxItems + 1).Select(i => ($"{i}.hl7", new byte[] { 77 })).ToArray());
        Assert.Throws<InvalidDataException>(() => MessageFile.Expand("many.zip", many));
        // DICOM recognised by its preamble, not its name.
        var dcm = new byte[200]; "DICM"u8.CopyTo(dcm.AsSpan(128));
        Assert.True(MessageFile.IsDicom(dcm));
        Assert.False(MessageFile.IsDicom(new byte[200]));
        // Legacy Latin-1 export still reads (macron-free names, but e.g. é).
        Assert.Equal("Renée", MessageFile.Text([0x52, 0x65, 0x6E, 0xE9, 0x65]));
    }

    [Fact]
    public async Task Monitor_file_from_while_the_gateway_was_down_is_filed_to_the_patient_connected_at_the_time_and_can_be_imported_twice()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var dev = sp.GetRequiredService<DeviceService>();
        var (e, p) = await DevicesTests.AdmittedAsync(h, sp);
        var (mon, _) = await dev.SaveMonitorAsync(null, Guid.NewGuid(), "PACU/BAY1", true, "admin");
        Assert.Null((await dev.ConnectAsync(mon!.Id, e.Id, "rn")).Error);
        var connectedAt = h.Clock.GetUtcNow();
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(await dev.DisconnectAsync((await sp.GetRequiredService<DevicesDbContext>().Associations.SingleAsync()).Id, "rn"));
        h.Clock.Advance(TimeSpan.FromMinutes(30)); // the file is imported later, once someone fetches the export

        // The gateway link was down: the monitor's export holds readings from during the connection and from after it.
        var file = DevicesTests.MonitorOru("PACU/BAY1", null, connectedAt.AddMinutes(10)) + DevicesTests.MonitorOru("PACU/BAY1", null, connectedAt.AddMinutes(20), hr: 90)
                   + DevicesTests.MonitorOru("PACU/BAY1", null, connectedAt.AddMinutes(45));
        var msgs = MessageFile.SplitHl7(file);
        Assert.Equal(3, msgs.Count);
        var results = new List<IngestResult>();
        foreach (var m in msgs) results.Add(await dev.ImportAsync(m));
        Assert.All(results, r => Assert.True(r.Accepted));
        Assert.Equal(p.Id, results[0].PatientId);
        Assert.Null(results[2].PatientId); // after disconnection: belongs to no one
        var db = sp.GetRequiredService<DevicesDbContext>();
        Assert.Equal(14, await db.Readings.CountAsync(r => r.AssociationId != null));
        Assert.Equal(7, await db.Readings.CountAsync(r => r.AssociationId == null));
        Assert.Null((await db.Monitors.SingleAsync()).LastSeenAt); // a file does not make the monitor look online

        // Importing the same file again files nothing twice.
        foreach (var m in msgs) Assert.Equal(0, (await dev.ImportAsync(m)).Stored);
        Assert.Equal(21, await db.Readings.CountAsync());
        Assert.False((await dev.ImportAsync(DevicesTests.MonitorOru("PACU/BAY7", null, connectedAt, deviceId: null))).Accepted);
    }

    [Fact]
    public void Recordings_are_recognised_by_content_not_file_name()
    {
        static byte[] B(string head, int at = 0) { var b = new byte[2000]; Encoding.ASCII.GetBytes(head).CopyTo(b, at); return b; }
        var wav = B("RIFF"); Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Assert.Equal("audio/wav", ScribeService.AudioType(wav));
        Assert.Equal("audio/mpeg", ScribeService.AudioType(B("ID3")));
        Assert.Equal("audio/mp4", ScribeService.AudioType(B("ftyp", 4)));
        Assert.Equal("audio/ogg", ScribeService.AudioType(B("OggS")));
        Assert.Null(ScribeService.AudioType(B("%PDF-1.4")));
    }
}

/// <summary>Real TDS steriliser cycle records (Sterilizer 1, cycles 00000217 and 00000218) read from USB.</summary>
public class SteriliserRecordTests
{
    private static string Fixture(string name) => System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void Real_steriliser_record_is_read_with_its_curve_and_plateau_checks()
    {
        var nz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        var r = BrightPAS.Modules.Assets.SteriliserLog.Parse(Fixture("steriliser-00000217.csv"))!;
        Assert.NotNull(r);
        Assert.Equal("134°C INSTRUMENTS", r.Program);
        Assert.Equal("Sterilizer 1", r.Machine);
        Assert.Equal("00000217", r.CycleNumber);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 9, 34, 9, TimeSpan.FromHours(12)), r.Start);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 10, 25, 8, TimeSpan.FromHours(12)), r.End);
        Assert.Equal(134.0m, r.SetTemperatureC); Assert.Equal(240, r.SetHoldSeconds); Assert.Equal(134.6m, r.MaxHoldC); Assert.Equal(134.2m, r.MinHoldC);
        Assert.True(r.Passed);
        Assert.Equal(764, r.Curve.Count);
        Assert.Equal(26.4m, r.Curve[0].TemperatureC); Assert.Equal(99.7m, r.Curve[0].PressureKpa);
        Assert.True(r.PlateauSeconds >= 240, $"plateau {r.PlateauSeconds}");
        Assert.InRange(r.PlateauPressureKpa!.Value, 300m, 320m);
        Assert.InRange(r.SaturationC!.Value, 133.5m, 136.5m);
        Assert.True(r.ChecksOk, string.Join(" ", r.Checks));
        // The generic log import uses it and the load can be released.
        var p = BrightPAS.Modules.Assets.CssdService.ParseMachineLog(Fixture("steriliser-00000218.csv"));
        Assert.Equal("00000218", p.MachineCycleNumber);
        Assert.True(p.Passed);
        Assert.Contains(p.Notes, n => n.StartsWith("Saturated steam", StringComparison.Ordinal));
    }

    [Fact]
    public void A_record_that_never_reached_temperature_fails_even_if_the_machine_said_passed()
    {
        var nz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        var text = Fixture("steriliser-00000217.csv");
        // Cap every curve temperature at 133.9 °C.
        var capped = string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d{4};\d{4};\d{4}$") && int.Parse(l[..4]) >= 1340 ? "1339" + l[4..] : l));
        var r = BrightPAS.Modules.Assets.SteriliserLog.Parse(capped)!;
        Assert.True(r.Passed);       // the machine's own result
        Assert.False(r.ChecksOk);    // BrightPAS's check
        Assert.Contains(r.Checks, c => c.Contains("NOT STERILE", StringComparison.Ordinal));
        Assert.False(BrightPAS.Modules.Assets.CssdService.ParseMachineLog(capped).Passed);
    }
}
