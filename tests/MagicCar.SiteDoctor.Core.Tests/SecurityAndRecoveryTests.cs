using System.Net;
using System.Text;
using System.Text.Json;
using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Diagnostics;
using MagicCar.SiteDoctor.Models;
using MagicCar.SiteDoctor.Recovery;
using MagicCar.SiteDoctor.Storage;

namespace MagicCar.SiteDoctor.Core.Tests;

public class SecurityAndRecoveryTests
{
    [Fact] public void First_artifact_is_read_only_even_if_config_requests_recovery()
    {
        Assert.False(new SiteDoctorOptions().Recovery.Enabled);
        Assert.False(RecoveryPolicy.BuildAllowsRecovery);
        Assert.All(RecoveryPolicy.Capabilities(true),c=>Assert.False(c.Enabled));
        Assert.All(Enum.GetValues<RecoveryActionId>(),a=>Assert.False(RecoveryPolicy.IsEnabled(a)));
    }
    [Theory]
    [InlineData(RecoveryActionId.RestartAutoPost)]
    [InlineData(RecoveryActionId.RestartPrintGateway)]
    [InlineData((RecoveryActionId)999)]
    public async Task Uninspected_or_unknown_actions_never_reach_executor(RecoveryActionId action)
    {
        var fake=new FakeRepair();var coordinator=new RecoveryCoordinator(fake,true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.RecoverAsync(action,_=>new Fixture().Run(),CancellationToken.None));
        Assert.Equal(0,fake.Calls);
    }
    [Fact] public async Task Default_coordinator_does_not_execute_any_repair()
    {
        var fake=new FakeRepair();var coordinator=new RecoveryCoordinator(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.RecoverAsync(RecoveryActionId.RestartSpooler,_=>new Fixture().Run(),CancellationToken.None));
        Assert.Equal(0,fake.Calls);
    }
    [Theory]
    [InlineData(RepairExecution.Requested)]
    [InlineData(RepairExecution.Failed)]
    [InlineData(RepairExecution.ElevationDenied)]
    [InlineData(RepairExecution.TimedOut)]
    public async Task Every_repair_outcome_triggers_retest_and_does_not_invent_success(RepairExecution execution)
    {
        var f=new Fixture();f.Http.Fail.Add(f.Options.TunnelReadinessUrl);var fake=new FakeRepair {Result=execution};
        var calls=0;var coordinator=new RecoveryCoordinator(fake,true);
        var outcome=await coordinator.RecoverAsync(RecoveryActionId.RestartCloudflared,async _=>{calls++;return await f.Run();},CancellationToken.None);
        Assert.Equal(1,calls);Assert.Equal(1,fake.Calls);Assert.False(outcome.Recovered);
        Assert.Equal(HealthStatus.Failed,outcome.Retest.Results.Single(r=>r.Component==ComponentId.Tunnel).Status);
    }
    [Fact] public async Task Restarted_spooler_does_not_make_offline_printer_healthy()
    {
        var f=new Fixture();f.System.Printer=f.System.Printer with {Offline=true};
        var outcome=await new RecoveryCoordinator(new FakeRepair(),true).RecoverAsync(RecoveryActionId.RestartSpooler,_=>f.Run(),CancellationToken.None);
        Assert.True(outcome.Recovered);Assert.Equal(HealthStatus.Failed,outcome.Retest.Printing);
    }
    [Fact] public async Task Successful_repair_requires_the_target_to_pass_its_health_check()
    {
        var outcome=await new RecoveryCoordinator(new FakeRepair(),true).RecoverAsync(RecoveryActionId.RestartCloudflared,_=>new Fixture().Run(),CancellationToken.None);
        Assert.True(outcome.Recovered);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"exec\",\"command\":\"anything\"}")]
    [InlineData("{\"type\":\"runDiagnostics\",\"extra\":true}")]
    [InlineData("{\"type\":\"repair\",\"action\":1}")]
    [InlineData("{\"type\":\"repair\",\"action\":\"1\"}")]
    [InlineData("{\"type\":\"repair\",\"action\":\"RestartInterBase\"}")]
    [InlineData("{\"type\":\"repair\",\"action\":\"RestartSpooler\",\"serviceName\":\"Other\"}")]
    [InlineData("{\"type\":\"ready\",\"type\":\"ready\"}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("bad json")]
    public void Malformed_or_extra_ui_commands_are_rejected(string json) => Assert.Null(UiProtocol.Parse(json,UiProtocol.DocumentUrl));
    [Theory]
    [InlineData("https://evil.example/index.html")]
    [InlineData("https://site-doctor.local/other.html")]
    [InlineData("https://site-doctor.local.evil.example/index.html")]
    [InlineData("http://site-doctor.local/index.html")]
    public void Untrusted_source_is_rejected(string source) => Assert.Null(UiProtocol.Parse("{\"type\":\"runDiagnostics\"}",source));
    [Theory]
    [InlineData("ready")]
    [InlineData("runDiagnostics")]
    [InlineData("openPrinterQueue")]
    public void Exact_read_only_commands_are_accepted(string type) => Assert.Equal(type,UiProtocol.Parse($"{{\"type\":\"{type}\"}}",UiProtocol.DocumentUrl)?.Type);

    [Theory]
    [InlineData("http://example.com/",false)]
    [InlineData("https://user:secret@example.com/",false)]
    [InlineData("https://example.com/?secret=value",false)]
    [InlineData("http://remote.example/health",true)]
    [InlineData("http://127.0.0.1/health?token=value",true)]
    public void Configuration_rejects_credentials_remote_local_probes_and_insecure_public_urls(string url,bool local)
    {
        var o=local?new SiteDoctorOptions {PrintGatewayHealthUrl=url}:new SiteDoctorOptions {InternetProbeUrl=url};
        Assert.Throws<InvalidDataException>(o.Validate);
    }
    [Fact] public void Configuration_accepts_the_reference_profile() => new SiteDoctorOptions().Validate();
    [Fact] public void Config_cannot_retarget_privileged_services()
    {
        var json="{\"spoolerServiceName\":\"OtherService\"}";
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<SiteDoctorOptions>(json,JsonDefaults.Options));
    }

    [Fact] public async Task Public_http_body_is_never_read()
    {
        var content=new ObservedContent();using var probe=new HttpProbe(2,new ReplyHandler(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=content}));
        var result=await probe.GetAsync("https://example.com/",false,CancellationToken.None);
        Assert.Null(result.Body);Assert.False(content.Read);
    }
    [Fact] public async Task Local_http_response_body_is_bounded()
    {
        var content=new StringContent(new string('x',20_000),Encoding.UTF8,"application/json");
        using var probe=new HttpProbe(2,new ReplyHandler(new HttpResponseMessage(HttpStatusCode.OK){Content=content}));
        await Assert.ThrowsAsync<InvalidDataException>(()=>probe.GetAsync("http://127.0.0.1/health",true,CancellationToken.None));
    }
    [Fact] public async Task Html_health_body_is_not_read_as_json()
    {
        var content=new ObservedContent();content.Headers.ContentType=new("text/html");
        using var probe=new HttpProbe(2,new ReplyHandler(new HttpResponseMessage(HttpStatusCode.OK){Content=content}));
        var result=await probe.GetAsync("http://127.0.0.1/health",true,CancellationToken.None);
        Assert.Null(result.Body);Assert.False(content.Read);
    }

    [Fact] public async Task History_is_bounded_and_preserves_last_run()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try
        {
            var store=new HistoryStore(dir);var run=await new Fixture().Run();
            for(var i=0;i<35;i++)await store.SaveRunAsync(run);
            var state=await store.LoadAsync();Assert.Equal(30,state.Recent.Count);Assert.Equal(run.Id,state.LastRun?.Id);
            Assert.DoesNotContain("SECRET",await File.ReadAllTextAsync(Path.Combine(dir,"state.json")));
        }
        finally {Directory.Delete(dir,true);}
    }
    [Fact] public async Task Corrupted_history_does_not_block_diagnostics()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        try {await File.WriteAllTextAsync(Path.Combine(dir,"state.json"),"invalid");Assert.Null((await new HistoryStore(dir).LoadAsync()).LastRun);}
        finally {Directory.Delete(dir,true);}
    }

    [Fact] public async Task Valid_json_with_invalid_history_shape_is_ignored()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        try {await File.WriteAllTextAsync(Path.Combine(dir,"state.json"),"{\"lastRun\":null,\"recent\":null}");Assert.Empty((await new HistoryStore(dir).LoadAsync()).Recent);}
        finally {Directory.Delete(dir,true);}
    }
    [Fact] public async Task Extra_private_fields_in_local_health_are_not_retained()
    {
        var f=new Fixture();f.Http.Responses[f.Options.AutoPostHealthUrl]=FakeHttp.Json("{\"ok\":true,\"token\":\"SYNTHETIC_SECRET\",\"customer\":\"SYNTHETIC_PRIVATE\"}");
        var json=JsonSerializer.Serialize(await f.Check(ComponentId.AutoPost),JsonDefaults.Options);
        Assert.DoesNotContain("SYNTHETIC_SECRET",json);Assert.DoesNotContain("SYNTHETIC_PRIVATE",json);
    }

    private sealed class ReplyHandler(HttpResponseMessage response):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(response); }
    private sealed class ObservedContent:HttpContent
    {
        public bool Read;
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context){Read=true;return Task.CompletedTask;}
    }
}
