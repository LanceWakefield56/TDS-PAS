using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;
using Interface = BrightPAS.Modules.Messaging.Interface;

namespace BrightPAS.Web.Tests;

/// <summary>0.12.0 messaging engine over HTTP: inbound REST interface, SQL connector, MCP server.</summary>
[Collection("fo-dicom")]
public class MessagingWebTests(PasWebFactory factory) : IClassFixture<PasWebFactory>
{
    private const string Pw = "correct horse battery staple";

    private async Task<HttpClient> SignInAsync(string userName, string role)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
            if (await users.FindByNameAsync(userName) is null)
            {
                var u = new PasUser { UserName = userName, DisplayName = userName };
                Assert.True((await users.CreateAsync(u, Pw)).Succeeded);
                await users.AddToRoleAsync(u, role);
            }
        }
        var c = factory.NewClient();
        await c.PostAsJsonAsync("/api/auth/login", new { userName, password = Pw });
        var begin = await (await c.PostAsJsonAsync("/api/auth/enrol/begin", new { })).Json();
        var done = await (await c.PostAsJsonAsync("/api/auth/enrol/complete", new { code = TestAuthenticator.Code(begin.GetProperty("sharedKey").GetString()!) })).Json();
        Assert.Equal("done", done.GetProperty("next").GetString());
        return c;
    }

    private async Task LicenceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<LicensingService>();
        var pem = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../licensing/dev/vendor-private-key.DEVELOPMENT-ONLY.pem"));
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(pem);
        var text = LicenceCodec.Sign(new LicenceDocument
        {
            LicenceId = "BP-MSG-" + Guid.NewGuid().ToString("N")[..6],
            Customer = "Test",
            Edition = "Sandbox",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresOn = new DateOnly(2030, 1, 1),
            InstallationCode = svc.InstallationCode(await svc.StateAsync()),
            Modules = ["*"],
            GraceDays = 0
        }, key);
        Assert.True((await svc.InstallAsync(text, "admin", null)).Ok);
    }

    [SqlServerFact]
    public async Task Rest_interface_needs_its_key_and_mcp_shows_status_without_content()
    {
        await LicenceAsync();
        using var ia = await SignInAsync("ia.web", DefaultRoles.IntegrationAdministrator);
        var key = (await (await ia.PostAsJsonAsync("/api/messaging/interfaces/generate-key", new { })).Json()).GetProperty("key").GetString()!;
        var save = await ia.PostAsJsonAsync("/api/messaging/interfaces", new
        {
            code = "partner-in",
            name = "Partner REST",
            direction = "Inbound",
            transport = "HttpReceive",
            format = "Hl7v2",
            handler = "None",
            secret = key,
            allowedClients = Array.Empty<object>(),
            isActive = true,
            alertAfterFailures = 3,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var anon = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string msg = "MSH|^~\\&|P|X|BrightPAS|TDS|202611101030||ADT^A08|M1|P|2.4\rPID|1||ZZZ0016^^^NHI||SECRETNAME^Aroha||19800517|F\r";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/integration/in/partner-in", new StringContent(msg))).StatusCode);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/integration/in/partner-in") { Content = new StringContent(msg, Encoding.UTF8, "application/hl7-v2") };
        req.Headers.Add("X-BrightPAS-Key", key);
        var res = await anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("MSA|AA|M1", await res.Content.ReadAsStringAsync());
        var list = await (await ia.GetAsync("/api/messaging/messages")).Json();
        Assert.Contains(list.EnumerateArray(), m => m.GetProperty("controlId").GetString() == "M1");

        // MCP: bearer token required; read-only status; no message content or names.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/mcp", new { jsonrpc = "2.0", id = 1, method = "initialize" })).StatusCode);
        var token = (await (await ia.PostAsJsonAsync("/api/messaging/mcp-clients", new { name = "Claude test", days = 7 })).Json()).GetProperty("token").GetString()!;
        async Task<JsonElement> Rpc(object body)
        {
            using var r = new HttpRequestMessage(HttpMethod.Post, "/api/mcp") { Content = JsonContent.Create(body) };
            r.Headers.Authorization = new("Bearer", token);
            var resp = await anon.SendAsync(r);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        }
        var init = await Rpc(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18" } });
        Assert.Equal("BrightPAS messaging engine", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        var tools = await Rpc(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        Assert.Contains(tools.GetProperty("result").GetProperty("tools").EnumerateArray(), t => t.GetProperty("name").GetString() == "engine_status");
        var status = await Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "engine_status", arguments = new { } } });
        var text = status.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("partner-in", text);
        var id = list.EnumerateArray().First(m => m.GetProperty("controlId").GetString() == "M1").GetProperty("id").GetString();
        var one = await Rpc(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "get_message", arguments = new { id } } });
        var oneText = one.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain("SECRETNAME", oneText);
        Assert.DoesNotContain("ZZZ0016", oneText);
        Assert.Contains("steps", oneText);
        var bad = await Rpc(new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "delete_everything", arguments = new { } } });
        Assert.True(bad.TryGetProperty("error", out _));

        // The built-in explanation and the (switched-off) AI assistant.
        var explain = await (await ia.GetAsync($"/api/messaging/messages/{id}/explain")).Json();
        Assert.True(explain.GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await ia.PostAsJsonAsync($"/api/messaging/messages/{id}/ai", new { })).StatusCode);
    }

    [SqlServerFact]
    public async Task Sql_connector_reads_new_rows_once_and_writes_with_parameters()
    {
        var cs = factory.ConnectionString;
        await using (var c = new SqlConnection(cs))
        {
            await c.OpenAsync();
            await new SqlCommand("""
                IF OBJECT_ID('dbo.ExtResults') IS NULL CREATE TABLE dbo.ExtResults (Id int IDENTITY PRIMARY KEY, Nhi nvarchar(10), Result nvarchar(20), UpdatedAt datetime2 NOT NULL);
                IF OBJECT_ID('dbo.ExtOut') IS NULL CREATE TABLE dbo.ExtOut (Id int IDENTITY PRIMARY KEY, Nhi nvarchar(10), Proc_ nvarchar(100), Amount decimal(10,2));
                INSERT dbo.ExtResults (Nhi, Result, UpdatedAt) VALUES ('ZZZ0016', '132', '2026-11-10T09:00:00'), ('ZZZ0024', '98', '2026-11-10T09:05:00');
                """, c).ExecuteNonQueryAsync();
        }
        const string select = "SELECT Nhi, Result, UpdatedAt FROM dbo.ExtResults WHERE (@watermark IS NULL OR UpdatedAt > @watermark) ORDER BY UpdatedAt";
        var rows = new List<string>();
        var (n, wm) = await SqlConnector.ReadAsync(cs, select, null, "UpdatedAt", 500, r => { rows.Add(r); return Task.CompletedTask; }, default);
        Assert.Equal(2, n);
        Assert.Contains("\"Result\":\"132\"", rows[0]);
        var (again, wm2) = await SqlConnector.ReadAsync(cs, select, wm, "UpdatedAt", 500, _ => Task.CompletedTask, default);
        Assert.Equal(0, again);
        Assert.Equal(wm, wm2);

        var written = await SqlConnector.WriteAsync(cs, "INSERT INTO dbo.ExtOut (Nhi, Proc_, Amount) VALUES (@nhi, @procedure, @amount)",
            """{ "nhi": "ZZZ0016", "procedure": "Colonoscopy'); DROP TABLE dbo.ExtOut;--", "amount": 4800.50 }""", default);
        Assert.Equal(1, written);
        await using var check = new SqlConnection(cs);
        await check.OpenAsync();
        var proc = (string)(await new SqlCommand("SELECT Proc_ FROM dbo.ExtOut WHERE Nhi = 'ZZZ0016'", check).ExecuteScalarAsync())!;
        Assert.StartsWith("Colonoscopy');", proc); // stored as text, never executed
    }

    [SqlServerFact]
    public async Task Dicom_node_answers_echo_stores_images_and_refuses_unregistered_devices()
    {
        await LicenceAsync();
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BrightPAS.Modules.Messaging.MessagingService>();
            var (i, errors) = await svc.SaveInterfaceAsync(new BrightPAS.Modules.Messaging.InterfaceEdit(null, "dicom-test", "DICOM test", BrightPAS.Modules.Messaging.InterfaceDirection.Inbound,
                BrightPAS.Modules.Messaging.Transport.DicomListen, BrightPAS.Modules.Messaging.MessageFormat.Dicom, BrightPAS.Modules.Messaging.InboundHandler.None, null, 11199, null, null, null, "BRIGHTPAS",
                [new BrightPAS.Modules.Messaging.ClientEdit("127.0.0.1", "ENDO1", "test tower")], true, 3, null, null), "test");
            Assert.True(i is not null, string.Join(" ", errors));
            id = i!.Id;
        }
        Interface iface;
        IReadOnlyList<BrightPAS.Modules.Messaging.AllowedClient> devices;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BrightPAS.Modules.Messaging.MessagingService>();
            iface = (await svc.InterfaceAsync(id))!;
            devices = await svc.AllowedClientsAsync(id);
        }
        FellowOakDicom.DicomSetupBuilder.UseServiceProvider(factory.Services);
        var ctx = new DicomScpContext(iface, devices.Select(d => (System.Net.IPAddress.Parse(d.Address), d.AeTitle!)).ToList(),
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<MessagingRuntime>(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        using var server = FellowOakDicom.Network.DicomServerFactory.Create<PasDicomScp>(11199, userState: ctx);

        var echoOk = false;
        var client = FellowOakDicom.Network.Client.DicomClientFactory.Create("127.0.0.1", 11199, false, "ENDO1", "BRIGHTPAS");
        await client.AddRequestAsync(new FellowOakDicom.Network.DicomCEchoRequest { OnResponseReceived = (_, r) => echoOk = r.Status == FellowOakDicom.Network.DicomStatus.Success });
        var ds = new FellowOakDicom.DicomDataset
        {
            { FellowOakDicom.DicomTag.SOPClassUID, FellowOakDicom.DicomUID.SecondaryCaptureImageStorage },
            { FellowOakDicom.DicomTag.SOPInstanceUID, FellowOakDicom.DicomUIDGenerator.GenerateDerivedFromUUID() },
            { FellowOakDicom.DicomTag.PatientID, "ZZZ0016" }, { FellowOakDicom.DicomTag.PatientName, "TEST^Dicom" }, { FellowOakDicom.DicomTag.AccessionNumber, "ACC-DCM-1" },
            { FellowOakDicom.DicomTag.Modality, "ES" }, { FellowOakDicom.DicomTag.StudyInstanceUID, FellowOakDicom.DicomUIDGenerator.GenerateDerivedFromUUID() },
        };
        var stored = FellowOakDicom.Network.DicomStatus.Pending;
        await client.AddRequestAsync(new FellowOakDicom.Network.DicomCStoreRequest(new FellowOakDicom.DicomFile(ds)) { OnResponseReceived = (_, r) => stored = r.Status });
        await client.SendAsync();
        Assert.True(echoOk);
        Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, stored);

        var rejected = false;
        var stranger = FellowOakDicom.Network.Client.DicomClientFactory.Create("127.0.0.1", 11199, false, "UNKNOWN", "BRIGHTPAS");
        stranger.AssociationRejected += (_, _) => rejected = true;
        await stranger.AddRequestAsync(new FellowOakDicom.Network.DicomCEchoRequest());
        try { await stranger.SendAsync(); } catch (FellowOakDicom.Network.DicomAssociationRejectedException) { rejected = true; }
        Assert.True(rejected);

        await using var check = factory.Services.CreateAsyncScope();
        var rows = await check.ServiceProvider.GetRequiredService<BrightPAS.Modules.Messaging.MessagingService>().SearchAsync(new BrightPAS.Modules.Messaging.MessageQuery(id));
        var m = Assert.Single(rows);
        Assert.Equal("ACC-DCM-1", m.Reference);
        Assert.Equal("DICOM ES", m.MessageType);
    }
}

public class TestBuildLicensingTests
{
    [Fact]
    public void Test_build_makes_every_built_module_available_without_a_licence()
    {
        var s = BrightPAS.Web.Licensing.LicensingService.TestBuildStatus();
        Assert.Equal(BrightPAS.Licensing.LicenceHealth.Valid, s.Health);
        Assert.Null(s.Licence);
        foreach (var m in BrightPAS.Licensing.ProductModules.All.Where(m => m.Available)) Assert.Equal(BrightPAS.Licensing.ModuleAccess.Full, s.AccessTo(m.Id));
        Assert.Equal(BrightPAS.Licensing.ModuleAccess.None, s.AccessTo("claims")); // not built yet
    }
}

public class LoggingTests
{
    [Fact]
    public void Secret_tokens_never_reach_the_log_and_log_lines_are_read_back_with_detail()
    {
        Assert.Equal("/api/my/***/open", BrightPAS.Web.Security.PasLogging.SafePath(new Microsoft.AspNetCore.Http.PathString("/api/my/SECRET-TOKEN/open")));
        Assert.Equal("/api/q/***", BrightPAS.Web.Security.PasLogging.SafePath(new Microsoft.AspNetCore.Http.PathString("/api/q/abc123")));
        Assert.Equal("/api/hooks/sms/***/inbound", BrightPAS.Web.Security.PasLogging.SafePath(new Microsoft.AspNetCore.Http.PathString("/api/hooks/sms/KEY/inbound")));
        Assert.Equal("/api/patients/123", BrightPAS.Web.Security.PasLogging.SafePath(new Microsoft.AspNetCore.Http.PathString("/api/patients/123")));

        var dir = Path.Combine(Path.GetTempPath(), "pas-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "BrightPAS-Web-20261110.log"),
            "2026-11-10 09:00:00.000 +13:00 [INF] BrightPAS.Requests #AB12CD34: GET /api/me answered 200\n" +
            "2026-11-10 09:00:01.000 +13:00 [ERR] BrightPAS.Requests #EF56AB78: Unexpected error EF56AB78 on POST /api/x for nurse\nSystem.InvalidOperationException: boom\n   at X.Y()\n");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:File:Path"] = dir }).Build();
        var errors = BrightPAS.Web.Security.PasLogging.Recent(config, "ERR", 10);
        var e = Assert.Single(errors);
        Assert.Contains("EF56AB78", e.Text);
        Assert.Contains("boom", e.Text);
        Assert.Equal(2, BrightPAS.Web.Security.PasLogging.Recent(config, "INF", 10).Count);
    }
}
