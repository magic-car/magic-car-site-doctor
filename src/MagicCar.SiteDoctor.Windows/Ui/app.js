'use strict';
(() => {
  const components = [
    ['Internet', 'الإنترنت', 'Internet connection', 'globe', 'network-path'],
    ['Cloudflare', 'Cloudflare', 'Edge · Access protection', 'cloud', 'network-path'],
    ['Tunnel', 'نفق الاتصال', 'Cloudflare Tunnel', 'link', 'network-path'],
    ['PrintGateway', 'بوابة الطباعة', 'Print Gateway · :8787', 'gateway', 'printing-nodes'],
    ['Spooler', 'خدمة طباعة Windows', 'Windows Print Spooler', 'stack', 'printing-nodes'],
    ['Printer', 'طابعة Epson', 'EPSON · L3250 Series', 'printer', 'printing-nodes'],
    ['AutoPost', 'واجهة العربي', 'AutoPost API · :5555', 'server', 'alarabi-nodes'],
    ['InterBase', 'خادم قاعدة البيانات', 'InterBase Server · :3050', 'database', 'alarabi-nodes'],
    ['Guardian', 'مراقب قاعدة البيانات', 'InterBase Guardian · supervisor', 'shield', 'alarabi-nodes']
  ];
  const labels = { NotChecked:'لم يُفحص', Checking:'جارٍ الفحص', Healthy:'يعمل', Warning:'تنبيه', Failed:'عطل' };
  const actionFor = { Tunnel:'RestartCloudflared', Spooler:'RestartSpooler', PrintGateway:'RestartPrintGateway', AutoPost:'RestartAutoPost' };
  const $ = id => document.getElementById(id);
  const state = { results:new Map(), busy:false, initialized:false, readOnly:true, selected:null, recovery:[], history:[], started:0 };
  const bridge = window.chrome?.webview;
  function send(message) { if (bridge && state.initialized) bridge.postMessage(message); }
  function icon(name, cls='') { const svg = document.createElementNS('http://www.w3.org/2000/svg','svg'); if(cls) svg.setAttribute('class',cls); const use=document.createElementNS(svg.namespaceURI,'use');use.setAttribute('href',`#i-${name}`);svg.append(use);return svg; }
  function element(tag, cls, text) { const el=document.createElement(tag); if(cls) el.className=cls; if(text!==undefined) el.textContent=text; return el; }
  function emptyResult(id) { return {component:id,status:'NotChecked',userMessage:'لم يُجرَ فحص لهذا المكوّن في هذه الجلسة.',evidence:{},durationMs:0}; }
  function buildNodes() {
    for (const [id,title,subtitle,symbol,parent] of components) {
      const button=element('button','node');button.dataset.component=id;button.id=`node-${id}`;button.dataset.status='NotChecked';
      const mark=element('span','node-icon');mark.append(icon(symbol));
      const text=element('span','node-text');text.append(element('span','node-title',title),element('span','node-subtitle',subtitle));
      button.append(mark,text,element('span','status-pill','لم يُفحص'),icon('arrow','node-arrow'));
      button.addEventListener('click',()=>{state.selected=id;renderDetails();$('details').showModal();});
      if(id==='Guardian')$(parent).append(element('span','dependency-label','خدمة إشرافية مرتبطة بالخادم'));
      $(parent).append(button);state.results.set(id,emptyResult(id));
    }
  }
  function setBusy(value) { state.busy=value;document.body.classList.toggle('busy',value);$('run-button').disabled=value||!state.initialized;$('run-button').querySelector('span').textContent=value?'جارٍ فحص النظام…':'بدء الفحص'; }
  function update(result) {
    if (!state.results.has(result.component) || !(result.status in labels)) return;
    state.results.set(result.component,result);const node=$(`node-${result.component}`);node.dataset.status=result.status;
    node.querySelector('.status-pill').textContent=labels[result.status];node.setAttribute('aria-label',`${components.find(c=>c[0]===result.component)[1]}: ${labels[result.status]}`);
    const count=[...state.results.values()].filter(r=>!['NotChecked','Checking'].includes(r.status)).length;$('progress-count').textContent=`${count} / 9`;
    $('local-notice').hidden=!['Internet','Cloudflare','Tunnel'].some(id=>state.results.get(id).status==='Failed');
    if(state.selected===result.component && $('details').open)renderDetails();
  }
  function branchStatus(id,status) { $(id).textContent=labels[status]??labels.NotChecked;$(id).dataset.status=status; }
  function renderDetails() {
    const id=state.selected; if(!id)return; const result=state.results.get(id);$('detail-title').textContent=components.find(c=>c[0]===id)[1];
    $('detail-status').textContent=labels[result.status];$('detail-status').dataset.status=result.status;$('detail-message').textContent=result.userMessage;
    $('evidence').replaceChildren();for(const [key,value]of Object.entries(result.evidence??{})){const row=element('div');row.append(element('dt','',key),element('dd','',String(value)));$('evidence').append(row);}
    $('evidence').hidden=!Object.keys(result.evidence??{}).length;
    $('checked-at').textContent=result.checkedAt?`آخر قراءة: ${formatDate(result.checkedAt)} · ${result.durationMs} ms`:'';
    $('queue-button').hidden=id!=='Printer';$('queue-button').disabled=state.busy||!state.initialized;
    const action=actionFor[id];const capability=state.recovery.find(x=>x.action===action);$('repair-button').hidden=!action;$('repair-button').disabled=state.busy||state.readOnly||!capability?.enabled;
    $('repair-reason').textContent=action?(capability?.reason??'إعادة التشغيل غير متاحة في هذه النسخة.') : id==='Printer'?'افتح قائمة الانتظار للاطلاع على المهام. لم يتم إرسال صفحة اختبار.':'';
  }
  function formatDate(date) { return new Date(date).toLocaleString('ar-PS',{month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'}); }
  function renderHistory() {
    $('history-count').textContent=String(state.history.length);$('history-list').replaceChildren();
    for (const entry of state.history.slice(-8).reverse()) {const li=element('li');li.append(element('span','',`${entry.kind==='recovery'?'إعادة تشغيل وإعادة فحص':'فحص النظام'} · ${labels[entry.status]??'غير معروف'}`),element('time','',formatDate(entry.at)));$('history-list').append(li);}
  }
  function showToast(message) { $('toast').textContent=message;$('toast').hidden=!message; }
  function onMessage(message) {
    if (!message || typeof message.type!=='string')return;
    switch(message.type) {
      case 'initialized':
        state.initialized=true;state.readOnly=message.readOnly!==false;state.recovery=message.recovery??[];state.history=message.history?.recent??[];
        $('machine').textContent=message.machine??'WINDOWS';$('mode-label').lastChild.textContent=state.readOnly?'تشخيص فقط':'تشخيص وإصلاح';
        if(message.history?.lastRun)$('last-check').textContent=`آخر فحص محفوظ: ${formatDate(message.history.lastRun.finishedAt)}`;
        renderHistory();setBusy(false);break;
      case 'runStarted':
        state.started=Date.now();showToast('');setBusy(true);for(const [id]of components)update(emptyResult(id));
        branchStatus('printing-status','Checking');branchStatus('alarabi-status','Checking');$('summary').dataset.status='Checking';
        $('summary-title').textContent='نقرأ حالة كل مكوّن…';$('summary-text').textContent='ستظهر النتائج تباعاً. يستمر الفحص المحلي حتى عند تعذّر الاتصال الخارجي.';$('issues').hidden=true;$('elapsed').textContent='الفحص قيد التنفيذ';break;
      case 'componentStatus':if(state.busy)update(message.result);break;
      case 'runCompleted': {
        const run=message.run; for(const result of run.results)update(result);$('progress-count').textContent=`${run.results.length} / 9`;setBusy(false);
        branchStatus('printing-status',run.printing);branchStatus('alarabi-status',run.alArabi);$('summary').dataset.status=run.overall;
        const issues=run.results.filter(r=>r.status!=='Healthy');const failures=issues.filter(r=>r.status==='Failed');
        $('summary-title').textContent=run.overall==='Healthy'?'الاختبارات المنفذة سليمة':failures.length===1?'مكوّن واحد يحتاج إلى انتباهك':failures.length?`${failures.length} مكوّنات تحتاج إلى انتباهك`:'توجد نتائج تحتاج إلى مراجعة';
        $('summary-text').textContent=run.overall==='Healthy'?'الاتصال والخدمات المحلية اجتازت الفحص. حالة الطابعة تمثل قائمة الانتظار في Windows.':issues.length===1?issues[0].userMessage:'راجع المكوّنات التالية؛ التفاصيل تساعدك على تحديد موضع المشكلة.';
        $('issues').replaceChildren();$('issues').hidden=issues.length<=1;
        for(const issue of issues){$('issues').append(element('li','',`${components.find(c=>c[0]===issue.component)[1]}: ${issue.userMessage}`));}
        $('elapsed').textContent=`اكتمل خلال ${Math.max(0,(new Date(run.finishedAt)-new Date(run.startedAt))/1000).toFixed(1)} ثانية`;
        $('last-check').textContent=`آخر فحص: ${formatDate(run.finishedAt)}`;state.history.push({kind:'diagnosis',at:run.finishedAt,status:run.overall});state.history=state.history.slice(-30);renderHistory();showToast(message.historyWarning??'');break;}
      case 'repairStarted':setBusy(true);showToast('بانتظار تنفيذ إعادة التشغيل ثم إعادة الفحص…');break;
      case 'repairCompleted':setBusy(false);showToast(message.outcome.recovered?'استجاب المكوّن بعد إعادة الفحص. راجع نتيجة النظام كاملة.':'لم يتم تأكيد نجاح الإصلاح. نتائج إعادة الفحص معروضة.');break;
      case 'operationError':setBusy(false);showToast(message.message);break;
    }
  }
  buildNodes();
  $('run-button').addEventListener('click',()=>{if(!state.busy){setBusy(true);send({type:'runDiagnostics'});}});
  $('close-details').addEventListener('click',()=>$('details').close());
  $('queue-button').addEventListener('click',()=>send({type:'openPrinterQueue'}));
  $('repair-button').addEventListener('click',()=>send({type:'repair',action:actionFor[state.selected]}));
  $('history-toggle').addEventListener('click',()=>{const open=$('history-list').hidden;$('history-list').hidden=!open;$('history-toggle').setAttribute('aria-expanded',String(open));});
  if(bridge){bridge.addEventListener('message',event=>onMessage(event.data));bridge.postMessage({type:'ready'});}
  else {showToast('افتح MagicCar.SiteDoctor.exe لتشغيل الفحوصات الحقيقية على Windows. هذه الصفحة وحدها لا تستطيع قراءة خدمات الجهاز.');}
})();
