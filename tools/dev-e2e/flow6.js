// 0.12.0 messaging engine walk-through: interfaces, mapping designer, flow canvas + test harness, live REST message, dashboard, viewer, explain, settings.
const ERR=[];
const {chromium}=require('playwright');const {adminContext,BASE,post,totp,H}=require('./lib');const fs=require('fs');
const shot=async(p,n)=>{await p.waitForTimeout(700);await p.screenshot({path:`/tmp/e6-${n}.png`,fullPage:true});console.log('shot',n);};
const get=async(ctx,p)=>{const r=await ctx.request.get(BASE+p,{headers:H});let j=null;try{j=await r.json();}catch{}return {status:r.status(),json:j};};
const ok=(r,what)=>{if(r.status>=300){console.log('FAIL',what,r.status,JSON.stringify(r.json));}return r;};
async function userCtx(b,admin,userName,display,role){const pw='correct horse battery staple';
  const c=await post(admin.request,'/api/admin/users',{userName,displayName:display,roles:[role],password:pw});if(c.status!==200)console.log('user',userName,JSON.stringify(c.json));
  const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1500,height:1000}});
  await post(ctx.request,'/api/auth/login',{userName,password:pw});const e=await post(ctx.request,'/api/auth/enrol/begin',{});
  const d=await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});if(d.json?.next!=='done')throw new Error('enrol '+userName);return ctx;}
process.on('unhandledRejection',()=>{});(async()=>{const b=await chromium.launch();const admin=await adminContext(b,process.argv[2]);
const watch=(pg,n)=>{pg.on('response',async r=>{if(r.url().includes('/api/')&&r.status()>=400){let t='';try{t=await r.text();}catch{}ERR.push(n+' '+r.status()+' '+r.request().method()+' '+r.url().replace(BASE,'')+' '+t.slice(0,200));}});pg.on('pageerror',e=>ERR.push(n+': '+e.message));pg.on('console',m=>{if(m.type()==='error')ERR.push(n+': '+m.text())});};
ok(await post(admin.request,'/api/admin/licence',{text:fs.readFileSync('/home/claude/repo/TDS-PAS/licensing/sandbox/BrightPAS-Sandbox-Licence.lic','utf8')}),'licence');
const ia=await userCtx(b,admin,'iris.integration','Iris Integration','Integration Administrator');
const ia2=await userCtx(b,admin,'ian.integration','Ian Integration','Integration Administrator');
console.log('setup messaging',(await post(admin.request,'/api/admin/modules/messaging/setup/complete',{})).status);
fs.mkdirSync('/tmp/e6-out',{recursive:true});
// ---- interfaces via the UI
const p=await ia.newPage();watch(p,'ia');await p.goto(BASE+'/messaging/interfaces');await p.waitForSelector('h1:has-text("Interfaces")');
await p.click('button:has-text("Add interface")');
await p.locator('label:has-text("Name") input').first().fill('Endoscopy DICOM via REST');await p.locator('label:has-text("Code") input').fill('endo-in');
await p.locator('label:has-text("How messages arrive") select').selectOption('HttpReceive');await p.locator('label:has-text("Message format") select').selectOption('Dicom');
await p.click('button:has-text("Generate key")');await p.waitForTimeout(400);const key=await p.locator('input[aria-label="Key"]').inputValue();
await shot(p,'01-interface-editor');await p.click('.side-panel button:has-text("Save")');await p.waitForSelector('.ok-text');
for(const [code,name,fmt,folder] of [['lab-oru-out','Lab system (HL7 files)','Hl7v2','/tmp/e6-out'],['dicom-copy','DICOM archive copy','Dicom','/tmp/e6-out']])
  ok(await post(ia.request,'/api/messaging/interfaces',{code,name,direction:'Outbound',transport:'FolderWrite',format:fmt,handler:'None',folderPath:folder,allowedClients:[],isActive:true,alertAfterFailures:3}),'iface '+code);
ok(await post(ia.request,'/api/messaging/interfaces',{code:'devices',name:'Endoscopy towers (DICOM)',direction:'Inbound',transport:'DicomListen',format:'Dicom',handler:'None',port:11112,aeTitle:'BRIGHTPAS',allowedClients:[{address:'127.0.0.1',aeTitle:'ENDO1',description:'Tower 1'}],isActive:true,alertAfterFailures:3}),'dicom listener');
await p.reload();await p.waitForSelector('td:has-text("Endoscopy towers")');await shot(p,'02-interfaces');
// ---- translation table
ok(await post(ia.request,'/api/messaging/tables',{name:'DICOM sex to HL7',description:'',whenMissing:'UseDefault',defaultValue:'U',entries:[{from:'F',to:'F'},{from:'M',to:'M'},{from:'O',to:'O'}]}),'table');
// ---- mapping created in the UI, rules via drag-and-drop
await p.goto(BASE+'/messaging/mappings');await p.click('button:has-text("New mapping")');
await p.locator('.side-panel label:has-text("Name") input').fill('Endoscopy DICOM → HL7 ORM');
await p.locator('.side-panel label:has-text("From") select').selectOption('Dicom');await p.locator('.side-panel label:has-text("To") select').selectOption('Hl7v2');
await p.click('.side-panel button:has-text("Create")');await p.waitForSelector('h2:has-text("Output preview")');
await p.locator('textarea[aria-label="Sample input"]').fill('');await p.click('button:has-text("Load example")');await p.waitForTimeout(1200);
// drag PatientID onto a new rule's source
const addRule=async(target,srcLabel)=>{await p.click('button:has-text("+ Rule")');const row=p.locator('table.rules tbody tr').last();await row.locator('input[aria-label="Target field"]').fill(target);
  await p.locator('.tree-label',{hasText:srcLabel}).first().dragTo(row.locator('input[aria-label="Source field"]'));};
await addRule('PID-3.1','PatientID');await addRule('PID-5','PatientName');await addRule('PID-7','PatientBirthDate');await addRule('OBR-3','AccessionNumber');
await addRule('OBR-4.2','StudyDescription');await addRule('MSH-10','AccessionNumber');await addRule('PID-8','PatientSex');
const r7=p.locator('table.rules tbody tr').last();await r7.locator('select[aria-label="How"]').selectOption('Translate');await p.waitForTimeout(200);
const tables=(await get(ia,'/api/messaging/tables')).json;await r7.locator('select[aria-label="Translation table"]').selectOption(tables[0].id);
await p.locator('textarea').nth(1).fill('MSH|^~\\&|BrightPAS|TDS|LAB|LAB|||ORM^O01||P|2.4\rPID|1\rORC|NW\rOBR|1\r');
await p.waitForTimeout(1500);await shot(p,'03-mapping-designer');
await p.click('button:has-text("Send for approval")');await p.waitForSelector('.ok-text');
const mapId=p.url().split('/').pop();
console.log('author approve disabled:',await p.locator('button:has-text("Approve and go live")').isDisabled());
const p2=await ia2.newPage();watch(p2,'ia2');await p2.goto(BASE+'/messaging/mappings/'+mapId);await p2.waitForSelector('button:has-text("Approve and go live")');
await p2.click('button:has-text("Approve and go live")');await p2.waitForSelector('.ok-text');await shot(p2,'04-mapping-approved');
// ---- flow drawn on the canvas
const ifs=(await get(ia,'/api/messaging/interfaces')).json;const I=c=>ifs.find(i=>i.code===c).id;
await p.goto(BASE+'/messaging/flows/new');await p.waitForSelector('svg.flow-canvas');
await p.locator('label:has-text("Flow name") input').fill('Endoscopy images to lab');
await p.click('.fnode.kind-Input');await p.locator('.card.subtle label:has-text("Interface") select').selectOption(I('endo-in'));
await p.click('button:has-text("+ Decision")');await p.locator('.card.subtle input[aria-label="Field"]').fill('Modality');await p.locator('.card.subtle input[aria-label="Value"]').fill('ES');
await p.locator('.card.subtle label:has-text("Label") input').fill('Is endoscopy?');
await p.click('button:has-text("+ Mapper")');await p.locator('.card.subtle label:has-text("Mapping") select').selectOption(mapId);
await p.click('button:has-text("+ Output")');await p.locator('.card.subtle label:has-text("Interface") select').selectOption(I('lab-oru-out'));await p.locator('.card.subtle label:has-text("Label") input').fill('Lab system');
await p.click('button:has-text("+ Output")');await p.locator('.card.subtle label:has-text("Interface") select').selectOption(I('dicom-copy'));await p.locator('.card.subtle label:has-text("Label") input').fill('Archive copy');
// connect by dragging ports
const box=async(sel)=>await p.locator(sel).boundingBox();
const connect=async(fromSel,toSel)=>{const a=await box(fromSel);const t=await box(toSel);await p.mouse.move(a.x+a.width/2,a.y+a.height/2);await p.mouse.down();await p.mouse.move(t.x+t.width/2,t.y+t.height/2,{steps:8});await p.mouse.up();};
await connect('.fnode.kind-Input .port.out','.fnode.kind-Decision .fnode-box');
await connect('.fnode.kind-Decision .port.out.Yes','.fnode.kind-Mapper .fnode-box');
await connect('.fnode.kind-Mapper .port.out','.fnode.kind-Output >> nth=0');
await connect('.fnode.kind-Decision .port.out.No','.fnode.kind-Output >> nth=1');
await p.click('button:has-text("Check")');await p.waitForTimeout(800);
console.log('check:',await p.locator('.ok-text, .error').first().textContent());
// test harness
await p.click('button:has-text("Load a DICOM sample")');await p.waitForTimeout(600);await p.click('button:has-text("Run test")');await p.waitForSelector('h3:has-text("Would send")');
await shot(p,'05-flow-canvas-test');
await p.locator('label:has-text("Switched on") input').check();await p.click('button:has-text("Save flow")');await p.waitForSelector('.ok-text');
// ---- live message through the REST interface
const sample=(await get(ia,'/api/messaging/samples/Dicom')).json.sample;
const r=await b.newContext({ignoreHTTPSErrors:true});const res=await r.request.post(BASE+'/api/integration/in/endo-in',{headers:{'X-BrightPAS-Key':key,'Content-Type':'application/dicom+json'},data:sample});
console.log('REST in:',res.status(),(await res.text()).slice(0,80));
const bad=await r.request.post(BASE+'/api/integration/in/endo-in',{headers:{'X-BrightPAS-Key':'wrong'},data:sample});console.log('wrong key:',bad.status());
await p.waitForTimeout(7000);console.log('files written:',fs.readdirSync('/tmp/e6-out').filter(f=>!f.startsWith('.')));
await p.goto(BASE+'/messaging');await p.waitForSelector('.iface-card');await shot(p,'06-dashboard');
await p.goto(BASE+'/messaging/messages');await p.waitForSelector('table.results');await p.locator('table.results tbody tr a').first().click();await p.waitForSelector('h2:has-text("What happened")');
await p.waitForTimeout(800);await shot(p,'07-message-viewer');
// ---- MCP token + AI settings
await p.goto(BASE+'/messaging/settings');await p.waitForSelector('h2:has-text("AI tool access")');await p.locator('label:has-text("Tool name") input').fill('Claude – IT support');await p.click('button:has-text("Create access token")');await p.waitForSelector('.copy-box');
const token=(await p.locator('.copy-box code').textContent()).trim();
const mcp=await r.request.post(BASE+'/api/mcp',{headers:{Authorization:'Bearer '+token},data:{jsonrpc:'2.0',id:1,method:'tools/call',params:{name:'engine_status',arguments:{}}}});
console.log('MCP status:',mcp.status(),(await mcp.text()).slice(0,140));
await shot(p,'08-settings-ai-mcp');
// dicom listener running?
const d=(await get(ia,'/api/messaging/dashboard')).json;console.log('dicom listener:',JSON.stringify(d.interfaces.find(x=>x.i.code==='devices').state));
console.log('ERRORS',JSON.stringify(ERR));await b.close();})().catch(e=>{console.error(e.message.split('\n')[0]);console.log('ERRORS',JSON.stringify(ERR));process.exit(1)});
