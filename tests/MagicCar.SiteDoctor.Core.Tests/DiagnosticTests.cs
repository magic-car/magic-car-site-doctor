using MagicCar.SiteDoctor.Configuration;
using MagicCar.SiteDoctor.Diagnostics;
using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor.Core.Tests;

public class DiagnosticTests
{
    [Fact] public async Task Reference_snapshot_fails_printing_even_when_gateway_and_spooler_pass()
    {
        var f=new Fixture();
        f.System.Printer=f.System.Printer with { Offline=true, Error=true, Status="Offline, Error", JobCount=1,
            Jobs=[new(true,true,true,false,DateTimeOffset.Now.AddMinutes(-30))] };
        var run=await f.Run();
        Assert.Equal(9,run.Results.Count);
        Assert.Equal(HealthStatus.Healthy,run.Results.Single(r=>r.Component==ComponentId.PrintGateway).Status);
        Assert.Equal(HealthStatus.Healthy,run.Results.Single(r=>r.Component==ComponentId.Spooler).Status);
        Assert.Equal(HealthStatus.Failed,run.Printing);
        Assert.Equal(HealthStatus.Healthy,run.AlArabi);
        Assert.Equal(HealthStatus.Failed,run.Overall);
    }

    [Theory]
    [InlineData(false,true,200,HealthStatus.Failed)]
    [InlineData(true,false,200,HealthStatus.Failed)]
    [InlineData(true,true,503,HealthStatus.Failed)]
    [InlineData(true,true,302,HealthStatus.Failed)]
    [InlineData(true,true,200,HealthStatus.Healthy)]
    public async Task Internet_requires_interface_dns_and_successful_https(bool network,bool dns,int code,HealthStatus expected)
    {
        var f=new Fixture();f.System.Network=network;f.System.Dns=dns;
        f.Http.Responses[f.Options.InternetProbeUrl]=new(code,"text/html",null,false);
        Assert.Equal(expected,(await f.Check(ComponentId.Internet)).Status);
    }

    [Theory]
    [InlineData(403,true,HealthStatus.Healthy)]
    [InlineData(403,false,HealthStatus.Warning)]
    [InlineData(200,true,HealthStatus.Warning)]
    [InlineData(302,true,HealthStatus.Warning)]
    [InlineData(500,true,HealthStatus.Failed)]
    [InlineData(502,true,HealthStatus.Failed)]
    [InlineData(401,true,HealthStatus.Failed)]
    public async Task Public_edge_respects_expected_access_result(int code,bool headers,HealthStatus expected)
    {
        var f=new Fixture();f.Http.Responses[f.Options.PublicEndpoints[0].Url]=new(code,"text/html","SECRET ACCESS HTML",headers);
        var result=await f.Check(ComponentId.Cloudflare);
        Assert.Equal(expected,result.Status);
        Assert.DoesNotContain("SECRET",string.Join("",result.Evidence.Values));
    }
    [Fact] public async Task Both_public_hosts_are_checked_after_first_host_fails()
    {
        var f=new Fixture();f.Http.Fail.Add(f.Options.PublicEndpoints[0].Url);
        Assert.Equal(HealthStatus.Failed,(await f.Check(ComponentId.Cloudflare)).Status);
        Assert.Contains(f.Options.PublicEndpoints[1].Url,f.Http.Calls);
    }
    [Fact] public async Task Later_warning_cannot_downgrade_an_edge_failure()
    {
        var f=new Fixture();f.Http.Responses[f.Options.PublicEndpoints[0].Url]=new(500,null,null,true);
        f.Http.Responses[f.Options.PublicEndpoints[1].Url]=new(200,null,null,true);
        Assert.Equal(HealthStatus.Failed,(await f.Check(ComponentId.Cloudflare)).Status);
    }

    [Theory]
    [InlineData(true,"Running",1,HealthStatus.Healthy)]
    [InlineData(true,"Running",4,HealthStatus.Healthy)]
    [InlineData(true,"Running",0,HealthStatus.Failed)]
    [InlineData(true,"Stopped",4,HealthStatus.Failed)]
    [InlineData(false,"Missing",4,HealthStatus.Failed)]
    public async Task Tunnel_needs_service_and_positive_ready_connections(bool exists,string state,int count,HealthStatus expected)
    {
        var f=new Fixture();f.System.Services[SiteDoctorOptions.CloudflaredService]=new(exists,state);
        f.Http.Responses[f.Options.TunnelReadinessUrl]=FakeHttp.Json($"{{\"status\":200,\"readyConnections\":{count}}}");
        Assert.Equal(expected,(await f.Check(ComponentId.Tunnel)).Status);
    }
    [Theory]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("{\"status\":503,\"readyConnections\":4}")]
    [InlineData("{\"status\":200}")]
    [InlineData("{\"status\":200,\"readyConnections\":-1}")]
    public async Task Tunnel_rejects_invalid_readiness(string body)
    {
        var f=new Fixture();f.Http.Responses[f.Options.TunnelReadinessUrl]=FakeHttp.Json(body);
        Assert.Equal(HealthStatus.Failed,(await f.Check(ComponentId.Tunnel)).Status);
    }

    [Theory]
    [InlineData(ComponentId.PrintGateway,"false","application/json",200)]
    [InlineData(ComponentId.PrintGateway,"malformed","application/json",200)]
    [InlineData(ComponentId.PrintGateway,"true","text/html",200)]
    [InlineData(ComponentId.PrintGateway,"true","application/json",503)]
    [InlineData(ComponentId.AutoPost,"false","application/json",200)]
    [InlineData(ComponentId.AutoPost,"malformed","application/json",200)]
    [InlineData(ComponentId.AutoPost,"true","text/html",200)]
    [InlineData(ComponentId.AutoPost,"true","application/json",404)]
    public async Task Local_apis_require_http_json_and_semantic_ok(ComponentId component,string ok,string type,int status)
    {
        var f=new Fixture();var url=component==ComponentId.PrintGateway?f.Options.PrintGatewayHealthUrl:f.Options.AutoPostHealthUrl;
        f.Http.Responses[url]=FakeHttp.Json($"{{\"ok\":{ok}}}",status,type);
        Assert.Equal(HealthStatus.Failed,(await f.Check(component)).Status);
    }
    [Fact] public async Task Gateway_printer_mismatch_is_failure()
    {
        var f=new Fixture();f.Http.Responses[f.Options.PrintGatewayHealthUrl]=FakeHttp.Json("{\"ok\":true,\"printer\":\"Other printer\"}");
        Assert.Equal(HealthStatus.Failed,(await f.Check(ComponentId.PrintGateway)).Status);
    }
    [Theory]
    [InlineData(false,"Missing")]
    [InlineData(true,"Ready")]
    [InlineData(true,"Disabled")]
    public async Task Usable_gateway_with_task_metadata_discrepancy_is_warning(bool exists,string state)
    {
        var f=new Fixture();f.System.Tasks[SiteDoctorOptions.PrintGatewayTask]=new(exists,state);
        Assert.Equal(HealthStatus.Warning,(await f.Check(ComponentId.PrintGateway)).Status);
    }
    [Theory]
    [InlineData(ComponentId.PrintGateway,8787)]
    [InlineData(ComponentId.AutoPost,5555)]
    [InlineData(ComponentId.InterBase,3050)]
    public async Task Required_port_closed_is_failure(ComponentId component,int port)
    {
        var f=new Fixture();f.System.Ports[port]=false;
        Assert.Equal(HealthStatus.Failed,(await f.Check(component)).Status);
    }

    [Theory]
    [InlineData(false,false,false,false,HealthStatus.Failed)]
    [InlineData(true,true,false,false,HealthStatus.Failed)]
    [InlineData(true,false,true,false,HealthStatus.Failed)]
    [InlineData(true,true,true,false,HealthStatus.Failed)]
    [InlineData(true,false,false,true,HealthStatus.Failed)]
    [InlineData(true,false,false,false,HealthStatus.Healthy)]
    public async Task Printer_checks_queue_independently(bool exists,bool offline,bool error,bool blocked,HealthStatus expected)
    {
        var f=new Fixture();f.System.Printer=f.System.Printer with {Exists=exists,Offline=offline,Error=error,Blocked=blocked};
        Assert.Equal(expected,(await f.Check(ComponentId.Printer)).Status);
    }
    [Theory]
    [InlineData(true,true,true,false,1,HealthStatus.Warning)]
    [InlineData(false,true,false,true,60,HealthStatus.Healthy)]
    [InlineData(false,false,true,false,60,HealthStatus.Warning)]
    [InlineData(false,false,true,false,1,HealthStatus.Healthy)]
    public async Task Print_jobs_distinguish_errors_stale_and_completed_retained(bool failed,bool retained,bool printing,bool complete,int minutes,HealthStatus expected)
    {
        var f=new Fixture();f.System.Printer=f.System.Printer with {JobCount=1,Jobs=[new(failed,retained,printing,complete,DateTimeOffset.Now.AddMinutes(-minutes))]};
        Assert.Equal(expected,(await f.Check(ComponentId.Printer)).Status);
    }
    [Fact] public async Task Unreadable_printer_is_not_claimed_healthy_or_confirmed_broken()
    {
        var f=new Fixture();f.System.PrinterError=new UnauthorizedAccessException("SECRET");
        var run=await f.Run();var result=run.Results.Single(r=>r.Component==ComponentId.Printer);
        Assert.Equal(HealthStatus.NotChecked,result.Status);Assert.Equal(HealthStatus.Warning,run.Overall);
        Assert.DoesNotContain("SECRET",string.Join("",result.Evidence.Values));
    }

    [Theory]
    [InlineData(0,HealthStatus.Failed)]
    [InlineData(1,HealthStatus.Healthy)]
    [InlineData(2,HealthStatus.Warning)]
    public async Task AutoPost_process_count_is_observed(int count,HealthStatus expected)
    { var f=new Fixture();f.System.Processes=count;Assert.Equal(expected,(await f.Check(ComponentId.AutoPost)).Status); }
    [Fact] public async Task Ready_watchdog_and_disabled_monitor_are_informational()
    {
        var f=new Fixture();f.System.Tasks[SiteDoctorOptions.WatchdogTask]=new(true,"Ready");f.System.Tasks[SiteDoctorOptions.MonitorTask]=new(true,"Disabled");
        Assert.Equal(HealthStatus.Healthy,(await f.Check(ComponentId.AutoPost)).Status);
    }
    [Theory]
    [InlineData(ComponentId.Spooler,SiteDoctorOptions.SpoolerService,false,"Missing",HealthStatus.Failed)]
    [InlineData(ComponentId.Spooler,SiteDoctorOptions.SpoolerService,true,"Stopped",HealthStatus.Failed)]
    [InlineData(ComponentId.Spooler,SiteDoctorOptions.SpoolerService,true,"Running",HealthStatus.Healthy)]
    [InlineData(ComponentId.InterBase,SiteDoctorOptions.InterBaseService,true,"Stopped",HealthStatus.Failed)]
    [InlineData(ComponentId.Guardian,SiteDoctorOptions.GuardianService,true,"Stopped",HealthStatus.Warning)]
    [InlineData(ComponentId.Guardian,SiteDoctorOptions.GuardianService,true,"Running",HealthStatus.Healthy)]
    public async Task Service_contracts_are_component_specific(ComponentId component,string name,bool exists,string state,HealthStatus expected)
    { var f=new Fixture();f.System.Services[name]=new(exists,state);Assert.Equal(expected,(await f.Check(component)).Status); }

    [Fact] public async Task Guardian_stopped_is_warning_but_server_stopped_is_branch_failure()
    {
        var f=new Fixture();f.System.Services[SiteDoctorOptions.GuardianService]=new(true,"Stopped");
        Assert.Equal(HealthStatus.Warning,(await f.Run()).AlArabi);
        f.System.Services[SiteDoctorOptions.InterBaseService]=new(true,"Stopped");
        Assert.Equal(HealthStatus.Failed,(await f.Run()).AlArabi);
    }
    [Fact] public async Task All_checks_run_after_network_tunnel_gateway_and_autopost_failures()
    {
        var f=new Fixture();f.System.Network=false;
        foreach(var url in new[]{f.Options.TunnelReadinessUrl,f.Options.PrintGatewayHealthUrl,f.Options.AutoPostHealthUrl})f.Http.Fail.Add(url);
        var run=await f.Run();Assert.Equal(9,run.Results.Count);Assert.Contains("Printer",f.System.Calls);
        Assert.Contains(SiteDoctorOptions.InterBaseService,f.System.Calls);Assert.Contains(SiteDoctorOptions.GuardianService,f.System.Calls);
        Assert.Equal(HealthStatus.Healthy,run.Results.Single(r=>r.Component==ComponentId.InterBase).Status);
    }
    [Fact] public async Task Concurrent_run_is_rejected_and_cancellation_releases_gate()
    {
        var f=new Fixture();f.Http.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator=new DiagnosticOrchestrator(f.Engine);using var ct=new CancellationTokenSource();
        var first=orchestrator.RunAsync(null,ct.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>orchestrator.RunAsync(null,CancellationToken.None));
        ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        f.Http.Hold=null;Assert.Equal(9,(await orchestrator.RunAsync(null,CancellationToken.None)).Results.Count);
    }
    [Fact] public void Empty_or_incomplete_result_is_never_healthy()
    {
        var run=new DiagnosticRun(Guid.NewGuid(),DateTimeOffset.Now,DateTimeOffset.Now,[]);
        Assert.NotEqual(HealthStatus.Healthy,run.Overall);Assert.NotEqual(HealthStatus.Healthy,run.Printing);
    }
}
