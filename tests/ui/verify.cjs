// This tests the HTML presentation with an explicitly synthetic host bridge.
// It does not test Windows, native WebView2, services, printers or UAC.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs/promises');
const { pathToFileURL } = require('node:url');
const root = path.resolve(__dirname, '../..');
const output = path.join(root, 'artifacts/ui-verification');
const url = pathToFileURL(path.join(root, 'src/MagicCar.SiteDoctor.Windows/Ui/index.html')).href;
let assertions = 0;
const check = (condition, message) => { assert.ok(condition, message); assertions++; };
const ids = ['Internet','Cloudflare','Tunnel','PrintGateway','Spooler','Printer','AutoPost','InterBase','Guardian'];
const reference = () => ({
  id:'00000000-0000-0000-0000-000000000001', startedAt:'2026-09-25T20:00:00Z', finishedAt:'2026-09-25T20:00:02Z',
  printing:'Failed', alArabi:'Healthy', overall:'Failed',
  results: ids.map(component => ({component,status:component==='Printer'?'Failed':'Healthy',
    userMessage:component==='Printer'?'طابعة Epson غير متصلة أو تحتاج تدخلاً. افحص الطاقة والورق والاتصال.':'اجتاز المكوّن الفحص في بيانات الاختبار الاصطناعية.',
    evidence:component==='Printer'?{Queue:'EPSON77D4D3 (L3250 Series)',Status:'Offline, Error',Jobs:'1',Scope:'Windows queue only; synthetic test fixture'}:
      component==='Cloudflare'?{'Al-Arabi':'HTTP 403 · expected','Printing':'HTTP 403 · expected'}:{State:'Synthetic healthy result'},
    durationMs:20, checkedAt:'2026-09-25T20:00:02Z'}))
});
(async () => {
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,...(process.env.SITE_DOCTOR_CHROME?{executablePath:process.env.SITE_DOCTOR_CHROME}:{})});
  try {
    const context=await browser.newContext({viewport:{width:1200,height:940},offline:true,reducedMotion:'reduce'});
    const page=await context.newPage();const errors=[];const remote=[];
    page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url()))remote.push(r.url());});
    await page.goto(url);
    check(await page.locator('#run-button').isDisabled(),'Standalone HTML must not pretend it can run native diagnostics');
    check(await page.locator('.node').count()===9,'Nine real component slots');
    check(await page.locator('[data-component][data-status="Healthy"]').count()===0,'No fake healthy state before a native run');
    await page.screenshot({path:path.join(output,'standalone-offline.png'),fullPage:true});
    await context.addInitScript(() => {
      window.__sent=[];
      window.chrome={webview:{
        postMessage(message){window.__sent.push(message);},
        addEventListener(type,callback){if(type==='message')window.__deliver=message=>callback({data:message});}
      }};
    });
    await page.reload();
    const emit=message=>page.evaluate(value=>window.__deliver(value),message);
    await emit({type:'initialized',readOnly:true,machine:'SYNTHETIC TEST FIXTURE',recovery:['RestartCloudflared','RestartSpooler','RestartPrintGateway','RestartAutoPost'].map(action=>({action,enabled:false,reason:'معطلة في نسخة التشخيص فقط.'})),history:{recent:[]}});
    check(await page.locator('#run-button').isEnabled(),'Native initialization enables diagnosis');
    await page.locator('#run-button').click();
    check(await page.locator('#run-button').isDisabled(),'Repeated clicks are blocked during a run');
    check(await page.evaluate(()=>window.__sent.filter(m=>m.type==='runDiagnostics').length)===1,'One diagnosis command sent');
    await emit({type:'runStarted'});
    await emit({type:'componentStatus',result:{component:'Internet',status:'Checking',userMessage:'جارٍ الفحص',evidence:{}}});
    check(await page.locator('#node-Internet').getAttribute('data-status')==='Checking','Real host event drives checking animation');
    const run=reference();for(const result of run.results)await emit({type:'componentStatus',result});
    await emit({type:'runCompleted',run});
    check(await page.locator('#printing-status').textContent()==='عطل','Printing aggregate is failed');
    check(await page.locator('#alarabi-status').textContent()==='يعمل','Al-Arabi remains healthy');
    check(await page.locator('#node-PrintGateway').getAttribute('data-status')==='Healthy','Gateway remains independently green');
    check(await page.locator('#node-Spooler').getAttribute('data-status')==='Healthy','Spooler remains independently green');
    check(await page.locator('#node-Printer').getAttribute('data-status')==='Failed','Printer is independently red');
    check((await page.locator('#summary-title').textContent()).includes('واحد'),'Summary identifies one failed component');
    check(await page.locator('#progress-count').textContent()==='9 / 9','Completion counter shows all checks');
    await page.screenshot({path:path.join(output,'reference-fixture.png'),fullPage:true});
    for(const id of ['Tunnel','Spooler','PrintGateway','AutoPost']){
      await page.locator(`#node-${id}`).click();check(await page.locator('#repair-button').isDisabled(),`${id} repair is disabled`);await page.locator('#close-details').click();
    }
    await page.locator('#node-Printer').click();
    check(await page.locator('#evidence').textContent().then(t=>t.includes('Offline, Error')),'Printer evidence is visible');
    check(await page.locator('#repair-button').isHidden(),'No physical printer restart');
    await page.locator('#queue-button').click();
    check(await page.evaluate(()=>window.__sent.some(m=>m.type==='openPrinterQueue')),'Queue action sends fixed ID');
    await page.screenshot({path:path.join(output,'printer-detail.png'),fullPage:true});
    await page.locator('#close-details').click();
    await emit({type:'runStarted'});
    const offline=reference();offline.results[0]={...offline.results[0],status:'Failed',userMessage:'الإنترنت غير متاح. سنواصل الفحص المحلي.'};
    await emit({type:'componentStatus',result:offline.results[0]});
    check(await page.locator('#local-notice').isVisible(),'Offline run explains continued local checks');
    check(await page.locator('#node-Printer').getAttribute('data-status')==='NotChecked','Rerun clears stale printer status');
    await emit({type:'runCompleted',run:offline});
    check(await page.locator('#node-InterBase').getAttribute('data-status')==='Healthy','Upstream failure does not hide healthy local results');
    await page.locator('#history-toggle').click();check(await page.locator('#history-list li').count()===2,'Bounded history is available');
    const unsafe=reference();unsafe.results[5].evidence={Status:'<img src=x onerror="window.__xss=1">'};
    await emit({type:'runCompleted',run:unsafe});await page.locator('#node-Printer').click();
    check(await page.locator('#evidence img').count()===0,'Evidence is text, never executable HTML');
    check(await page.evaluate(()=>window.__xss)===undefined,'Untrusted evidence cannot execute script');
    await page.keyboard.press('Escape');check(!await page.locator('#details').isVisible(),'Dialog supports Escape');
    for(const width of [880,1200,390]){await page.setViewportSize({width,height:940});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`No horizontal overflow at ${width}px`);}
    check(errors.length===0,`No JavaScript runtime errors: ${errors.join('; ')}`);
    check(remote.length===0,'Offline interface makes no HTTP requests');
    const report={assertions,failed:0,environment:'Linux headless Chromium; synthetic bridge only',nativeWindowsVerified:false,remoteRequests:remote.length,errors};
    await fs.writeFile(path.join(output,'results.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
    await context.close();
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
