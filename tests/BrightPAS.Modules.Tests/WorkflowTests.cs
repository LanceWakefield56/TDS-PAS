using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Messaging;

namespace BrightPAS.Modules.Tests;

/// <summary>0.35.0 workflow engine (tasks, waits, escalation) and MLLP/DICOM over TLS (ADR 0048).</summary>
public class WorkflowTests
{
    private const string Oru = "MSH|^~\\&|LAB|AWANUI|BrightPAS|TDS|202611101030||ORU^R01|MSG0001|P|2.4\r" +
                               "PID|1||ZZZ0016^^^NHI^NHI||TEST^Aroha^Mere||19800517|F\r" +
                               "OBR|1|ORD123|ACC456|HB^Haemoglobin^L\r" +
                               "OBX|1|NM|HB^Haemoglobin^L||182|g/L|115-155|H|||F\r";

    [Fact]
    public async Task Flow_makes_a_task_holds_a_message_then_sends_and_overdue_tasks_escalate()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MessagingService>();
        var flows = sp.GetRequiredService<FlowService>();
        var work = sp.GetRequiredService<WorkflowService>();
        var (inp, e1) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "lab-api", "Lab via REST", InterfaceDirection.Inbound, Transport.HttpReceive, MessageFormat.Hl7v2,
            InboundHandler.None, null, null, null, null, "k" + new string('x', 30), null, null, true, 3, null, null), "ia");
        Assert.True(inp is not null, string.Join(" ", e1));
        var (outp, e2) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "copy-out", "Copy", InterfaceDirection.Outbound, Transport.FolderWrite, MessageFormat.Hl7v2,
            InboundHandler.None, null, null, "/tmp/out", null, null, null, null, true, 3, null, null), "ia");
        Assert.True(outp is not null, string.Join(" ", e2));

        NodeEdit N(string k, NodeKind kind, Guid? iface = null, params FilterEdit[] c) => new(k, kind, k, 0, 0, iface, null, MatchMode.All, c);
        var task = N("task", NodeKind.Task) with { TaskTitle = "Abnormal result for {PID-5.1} – review", TaskRole = "Nurse", DueMinutes = 30, EscalateToRole = "Practice Manager" };
        var wait = N("wait", NodeKind.Wait) with { WaitMinutes = 60 };
        var flow = new FlowEdit(null, "Abnormal results", null, true,
            [N("in", NodeKind.Input, inp!.Id), N("high", NodeKind.Decision, c: new FilterEdit("OBX-8", ConditionOperator.Equals, "H")), task, wait, N("out", NodeKind.Output, outp!.Id)],
            [new("in", "high", EdgeBranch.Next), new("high", "task", EdgeBranch.Yes), new("high", "wait", EdgeBranch.No), new("wait", "out", EdgeBranch.Next)]);

        // Checker: a task needs a role; a wait needs a time.
        var bad = await flows.CheckAsync([.. flow.Nodes!.Select(n => n.Key == "task" ? n with { TaskRole = null } : n.Key == "wait" ? n with { WaitMinutes = 0 } : n)], flow.Edges!);
        Assert.Contains(bad, x => x.Contains("who does it", StringComparison.Ordinal));
        Assert.Contains(bad, x => x.Contains("wait between", StringComparison.Ordinal));

        // Test harness: shows the task it would make (title filled from the message) and the wait it would do.
        var t1 = await flows.TestAsync(flow, Oru, false);
        Assert.Empty(t1.Errors);
        Assert.Contains(t1.Steps, x => x.Kind == "Task" && x.Outcome == "Would make a task" && x.Detail!.Contains("Abnormal result for TEST", StringComparison.Ordinal));
        var t2 = await flows.TestAsync(flow, Oru.Replace("|H|||F", "|N|||F"), false);
        Assert.Contains(t2.Steps, x => x.Kind == "Wait" && x.Outcome == "Would wait");
        Assert.Single(t2.Outputs);
        var (saved, errs) = await flows.SaveAsync(flow, "ia");
        Assert.True(saved is not null, string.Join(" ", errs));
        Assert.Equal("Nurse", (await flows.FlowAsync(saved!.Id))!.Nodes.Single(n => n.Kind == NodeKind.Task).TaskRole);

        // Live, high result: a task for nurses, nothing sent.
        var m1 = await svc.ReceiveAsync(inp, Oru, "test");
        await flows.ProcessAsync(m1, svc.ContentOf(m1));
        var nurse = new[] { "Nurse" };
        var mine = await work.ListAsync("mine", nurse, "nurse1", false);
        var tk = Assert.Single(mine);
        Assert.Equal(("T000001", "Abnormal result for TEST – review", m1.Id), (tk.Number, tk.Title, tk.MessageId!.Value));
        Assert.Empty(await work.ListAsync("mine", ["Reception / Booking"], "rec", false));
        Assert.Equal(1, (await work.CountsAsync(nurse, "nurse1")).Mine);

        // Live, normal result: held for an hour, then sent.
        var m2 = await svc.ReceiveAsync(inp, Oru.Replace("|H|||F", "|N|||F").Replace("MSG0001", "MSG0002"), "test");
        Assert.Equal(0, await flows.ProcessAsync(m2, svc.ContentOf(m2)));
        Assert.Empty(await svc.TakeDueAsync());
        Assert.Equal(0, await flows.ResumeDueAsync());
        h.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(1, await flows.ResumeDueAsync());
        Assert.Single(await svc.TakeDueAsync());

        // The task is now overdue (30 minutes): it moves to the practice manager, urgent, once.
        var esc = Assert.Single(await work.EscalateAsync());
        Assert.Equal(("Practice Manager", "Nurse", WorkTaskPriority.Urgent), (esc.Role, esc.EscalatedFrom, esc.Priority));
        Assert.Empty(await work.EscalateAsync());
        Assert.Empty(await work.ListAsync("mine", nurse, "nurse1", false));
        var pm = new[] { "Practice Manager" };
        Assert.Single(await work.ListAsync("overdue", pm, "pm", false));
        Assert.Null(await work.TakeAsync(tk.Id, false, pm, "pm", false));
        Assert.Contains("already doing", await work.TakeAsync(tk.Id, false, pm, "pm2", false));
        Assert.Contains("why", await work.FinishAsync(tk.Id, false, null, pm, "pm", false));
        Assert.Null(await work.FinishAsync(tk.Id, true, "Rang the patient; GP told", pm, "pm", false));
        Assert.Single(await work.ListAsync("done", pm, "pm", false));
        Assert.Contains("already finished", await work.FinishAsync(tk.Id, true, null, pm, "pm", false));

        // Staff can make a task by hand.
        var roles = new HashSet<string> { "Nurse", "Practice Manager" };
        Assert.Contains((await work.CreateAsync(new TaskEdit("x", null, "Nurse", null, null), roles, "rec")).Errors, x => x.Contains("3–200", StringComparison.Ordinal));
        Assert.Contains((await work.CreateAsync(new TaskEdit("Call the lab", null, "Nurse", null, "Practice Manager"), roles, "rec")).Errors, x => x.Contains("time to do it by", StringComparison.Ordinal));
        var (made, _) = await work.CreateAsync(new TaskEdit("Call the lab about the missing sample", "Mrs Test, ACC456", "Nurse", 120, null, WorkTaskPriority.Urgent), roles, "rec");
        Assert.Equal("T000002", made!.Number);
        Assert.Single(await work.ListAsync("all", ["Reception / Booking"], "rec", false)); // the person who made it can see it
    }

    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx), null);
    }

    [Fact]
    public async Task Mllp_over_tls_works_with_a_pinned_certificate_and_refuses_the_wrong_one()
    {
        using var server = SelfSigned("brightpas-test");
        using var other = SelfSigned("someone-else");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            async Task<string?> ServeOnceAsync()
            {
                using var c = await listener.AcceptTcpClientAsync();
                try
                {
                    await using var ssl = await TlsSupport.AcceptAsync(c.GetStream(), server, null, CancellationToken.None);
                    var msg = await BrightPAS.Messaging.Hl7.Mllp.ReadAsync(ssl, 100_000, CancellationToken.None);
                    await ssl.WriteAsync(BrightPAS.Messaging.Hl7.Mllp.Frame("MSH|^~\\&|BrightPAS|TDS|LAB|AWANUI|202611101031||ACK|A1|P|2.4\rMSA|AA|MSG0001\r"));
                    return msg;
                }
                catch (Exception ex) when (ex is IOException or AuthenticationException) { return null; }
            }

            // Right pin: the message arrives encrypted and the ACK comes back.
            var serve = ServeOnceAsync();
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                await using var ssl = await TlsSupport.ConnectAsync(client.GetStream(), "localhost", null, server.Thumbprint, CancellationToken.None);
                Assert.True(ssl.SslProtocol is SslProtocols.Tls12 or SslProtocols.Tls13);
                await ssl.WriteAsync(BrightPAS.Messaging.Hl7.Mllp.Frame(Oru));
                var ack = await BrightPAS.Messaging.Hl7.Mllp.ReadAsync(ssl, 100_000, CancellationToken.None);
                Assert.Contains("MSA|AA", ack, StringComparison.Ordinal);
            }
            Assert.Contains("ORU^R01", await serve, StringComparison.Ordinal);

            // Wrong pin (or an untrusted self-signed certificate without a pin): refused before anything is sent.
            foreach (var pin in new[] { other.Thumbprint, null })
            {
                var serve2 = ServeOnceAsync();
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await Assert.ThrowsAsync<AuthenticationException>(() => TlsSupport.ConnectAsync(client.GetStream(), "localhost", null, pin, CancellationToken.None));
                client.Close();
                Assert.Null(await serve2);
            }
        }
        finally { listener.Stop(); }

        Assert.Contains("needs BrightPAS's certificate", TlsSupport.Check(true, null, null));
        Assert.Contains("40 letters", TlsSupport.Check(false, null, "ABC"));
        Assert.Null(TlsSupport.Check(false, null, server.Thumbprint));
        Assert.Equal(server.Thumbprint, TlsSupport.Normalise(string.Join(' ', server.Thumbprint.Chunk(2).Select(x => new string(x).ToLowerInvariant()))));
    }

    [Fact]
    public async Task Tls_settings_are_checked_when_an_interface_is_saved()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<MessagingService>();
        var http = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "fhir-out", "FHIR", InterfaceDirection.Outbound, Transport.HttpPost, MessageFormat.Json,
            InboundHandler.None, null, null, null, "https://partner.example.nz/fhir", null, null, null, true, 2, null, null, UseTls: true), "ia");
        Assert.Contains(http.Errors, e => e.Contains("already use https", StringComparison.Ordinal));
        var listen = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "lab-tls", "Lab TLS", InterfaceDirection.Inbound, Transport.MllpListen, MessageFormat.Hl7v2,
            InboundHandler.None, null, 2575, null, null, null, null, [new ClientEdit("10.0.0.5", null, "lab")], true, 3, null, null, UseTls: true), "ia");
        Assert.Contains(listen.Errors, e => e.Contains("needs BrightPAS's certificate", StringComparison.Ordinal));
        var send = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "lab-send", "Lab send", InterfaceDirection.Outbound, Transport.MllpSend, MessageFormat.Hl7v2,
            InboundHandler.None, "lab.example.nz", 2575, null, null, null, null, null, true, 3, null, null, UseTls: true, PeerThumbprint: new string('a', 40)), "ia");
        Assert.True(send.Interface is not null, string.Join(" ", send.Errors));
        Assert.Equal((true, new string('A', 40)), (send.Interface!.UseTls, send.Interface.PeerThumbprint));
        Assert.True((await svc.InterfacesAsync()).Single(i => i.Code == "lab-send").UseTls);
    }
}
