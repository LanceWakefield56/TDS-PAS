using System.IO.Compression;
using System.Text;

namespace BrightPAS.Messaging;

/// <summary>One item taken out of an imported file: where it came from (file, or file › entry in a ZIP) and its bytes.</summary>
public sealed record FileItem(string Name, byte[] Bytes);

/// <summary>
/// Offline import (0.18.0): when a device or interface connection is down, its messages arrive as files – copied from a USB stick,
/// exported to a network share or saved by the sending system. This class turns such files into the individual messages the live
/// connection would have delivered, safely: ZIP archives are opened with entry-count and size limits (no zip bombs, no paths written
/// to disk), HL7 files may hold many messages (one per MSH, with or without MLLP framing or FHS/BHS batch envelopes).
/// </summary>
public static class MessageFile
{
    /// <summary>Most files one import may contain (after opening ZIPs).</summary>
    public const int MaxItems = 2000;
    /// <summary>Largest single file or ZIP entry.</summary>
    public const long MaxItemBytes = 500L * 1024 * 1024;
    /// <summary>Most bytes a ZIP may expand to in total.</summary>
    public const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Opens ZIP archives (one level) and returns every file inside, or the file itself when it is not a ZIP.</summary>
    public static IReadOnlyList<FileItem> Expand(string name, byte[] bytes)
    {
        if (!IsZip(bytes)) return [new FileItem(name, bytes)];
        var items = new List<FileItem>();
        long total = 0;
        using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/') || e.Length == 0) continue; // folder
            var entryName = Path.GetFileName(e.FullName);
            if (entryName.StartsWith('.') || entryName.Equals("DICOMDIR", StringComparison.OrdinalIgnoreCase)) continue; // index / hidden files
            if (items.Count >= MaxItems) throw new InvalidDataException($"The ZIP holds more than {MaxItems} files – split it into smaller ZIPs.");
            if (e.Length > MaxItemBytes) throw new InvalidDataException($"{e.FullName} is larger than {MaxItemBytes / 1024 / 1024} MB.");
            total += e.Length;
            if (total > MaxExpandedBytes) throw new InvalidDataException("The ZIP expands to more than 2 GB – split it into smaller ZIPs.");
            using var s = e.Open();
            using var ms = new MemoryStream((int)Math.Min(e.Length, int.MaxValue));
            // Read at most the declared length + 1 byte, so a ZIP that lies about its sizes cannot expand without limit.
            var buffer = new byte[81920];
            int n;
            while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (ms.Length > e.Length || ms.Length > MaxItemBytes) throw new InvalidDataException($"{e.FullName} is larger than its ZIP header says – not imported.");
            }
            items.Add(new FileItem($"{name} › {e.FullName}", ms.ToArray()));
        }
        return items;
    }

    public static bool IsZip(byte[] b) => b.Length > 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;

    /// <summary>A DICOM Part 10 file ("DICM" at byte 128).</summary>
    public static bool IsDicom(byte[] b) => b.Length > 132 && b[128] == (byte)'D' && b[129] == (byte)'I' && b[130] == (byte)'C' && b[131] == (byte)'M';

    /// <summary>
    /// Splits an HL7 v2 file into messages: removes MLLP framing (VT … FS CR) and batch envelopes (FHS, BHS, BTS, FTS), accepts CR, LF or CRLF
    /// line ends, and starts a new message at every MSH segment. Each message is returned with CR segment separators, as MLLP would deliver it.
    /// </summary>
    public static IReadOnlyList<string> SplitHl7(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..]; // byte-order mark from Windows editors
        var messages = new List<string>();
        StringBuilder? current = null;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim('\v', '\x1c', '\x00', ' ', '\t');
            if (line.Length < 3) continue;
            if (line.StartsWith("FHS", StringComparison.Ordinal) || line.StartsWith("BHS", StringComparison.Ordinal)
                || line.StartsWith("BTS", StringComparison.Ordinal) || line.StartsWith("FTS", StringComparison.Ordinal)) continue;
            if (line.StartsWith("MSH", StringComparison.Ordinal))
            {
                if (current is not null) messages.Add(current.ToString());
                current = new StringBuilder();
            }
            if (current is null) continue; // text before the first MSH (headers, notes) is not part of a message
            current.Append(line).Append('\r');
        }
        if (current is not null) messages.Add(current.ToString());
        return messages;
    }

    /// <summary>Reads text the way sending systems write it: UTF-8 (with or without BOM), falling back to Latin-1 for legacy exports.</summary>
    public static string Text(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
