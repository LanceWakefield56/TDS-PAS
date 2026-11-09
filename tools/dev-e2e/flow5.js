const ERR=[];
const {chromium}=require('playwright');const {adminContext,BASE,post,totp,H}=require('./lib');const fs=require('fs');
const shot=async(p,n)=>{await p.waitForTimeout(600);await p.screenshot({path:`/tmp/e5-${n}.png`,fullPage:true});console.log('shot',n);};
async function userCtx(b,admin,userName,display,role){const pw='correct horse battery staple';
  const c=await post(admin.request,'/api/admin/users',{userName,displayName:display,roles:[role],password:pw});if(c.status!==200)console.log('user',userName,JSON.stringify(c.json));
  const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1400,height:1000},acceptDownloads:true});
  await post(ctx.request,'/api/auth/login',{userName,password:pw});const e=await post(ctx.request,'/api/auth/enrol/begin',{});
  const d=await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});if(d.json?.next!=='done')throw new Error('enrol '+userName+JSON.stringify(d));return ctx;}
const nzToday=()=>new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Auckland'});
const add=(iso,n)=>{const d=new Date(iso+'T00:00Z');d.setUTCDate(d.getUTCDate()+n);return d.toISOString().slice(0,10);};
const get=async(ctx,p)=>{const r=await ctx.request.get(BASE+p,{headers:H});let j=null;try{j=await r.json();}catch{}return {status:r.status(),json:j};};
const ok=(r,what)=>{if(r.status>=300){console.log('FAIL',what,r.status,JSON.stringify(r.json));}return r;};
process.on('unhandledRejection',()=>{});(async()=>{const b=await chromium.launch();const admin=await adminContext(b,process.argv[2]);const errs=ERR;
const watch=(pg,n)=>{pg.on('response',async r=>{if(r.url().includes('/api/')&&r.status()>=400){let t='';try{t=await r.text();}catch{}errs.push(n+' '+r.status()+' '+r.request().method()+' '+r.url().replace(BASE,'')+' '+t.slice(0,200));}});pg.on('pageerror',e=>errs.push(n+': '+e.message));pg.on('console',m=>{if(m.type()==='error')errs.push(n+': '+m.text())});};
ok(await post(admin.request,'/api/admin/licence',{text:fs.readFileSync('/home/claude/repo/TDS-PAS/licensing/sandbox/BrightPAS-Sandbox-Licence.lic','utf8')}),'licence');
await post(admin.request,'/api/admin/organisation/procedures/examples',{});
const rec=await userCtx(b,admin,'sam.reception','Sam Reception','Reception / Booking');
const priv=await userCtx(b,admin,'lance.privacy','Lance Privacy','Privacy Officer');
const mgr=await userCtx(b,admin,'lynda.roster','Lynda Roster','Roster Manager');
const nurse=await userCtx(b,admin,'jacqui.nurse','Jacqui Nurse','Nurse');
const nurse2=await userCtx(b,admin,'mere.nurse','Mere Nurse','Nurse');
const spec=await userCtx(b,admin,'ben.hall','Dr Ben Hall','Specialist (portal only)');
for(const c of [['Dr Ben Hall','Surgeon','ben.hall'],['Dr Kate Ford','Anaesthetist',null]]) ok(await post(admin.request,'/api/admin/organisation/clinicians',{name:c[0],type:c[1],userName:c[2],isActive:true}),'clin');
const clins=(await get(admin,'/api/organisation/clinicians')).json;const hall=clins.find(c=>c.name==='Dr Ben Hall');const kate=clins.find(c=>c.name==='Dr Kate Ford');
// credentialing: APC expiring in 20 days, endoscopy scope only
const t=nzToday();
for(const [ty,exp] of [['Apc',add(t,20)],['Indemnity',add(t,300)],['CredentialingApproval',add(t,900)]]) ok(await post(admin.request,'/api/credentialing/credentials',{clinicianId:hall.id,type:ty,reference:ty+'-1',issuer:'MCNZ',issuedOn:null,expiresOn:exp,notes:null,isActive:true}),'cred');
ok(await post(admin.request,'/api/credentialing/privileges',{clinicianId:hall.id,service:'Endoscopy',procedureCode:null,approvedOn:add(t,-300),reviewDue:null,conditions:null,isActive:true}),'priv');
for(const m of ['theatre','credentialing','portal']){const r=await post(admin.request,`/api/admin/modules/${m}/setup/complete`,{});console.log('setup',m,r.status,JSON.stringify(r.json).slice(0,200));}
// patients
const mk=async(f,g,nhi,dob)=>(ok(await post(rec.request,'/api/patients',{familyName:f,givenNames:g,dateOfBirth:dob,sex:'Female',nhi,confirmedNotDuplicate:true,mobilePhone:'0215550123',email:'sarah@example.com'}),'pt')).json.id;
const p1=await mk('Carpenter','Sarah',null,'1975-03-01');const dup=await mk('Carpentar','Sarah',null,'1975-03-01');const p3=await mk('Jordan','Melissa','ZAA0067','1980-02-02');
await post(rec.request,`/api/patients/${p1}/alerts`,{type:'Allergy',severity:'Critical',description:'Penicillin – anaphylaxis'});
// theatre: list for Dr Ben Hall in 30 days -> lap chole => credential warnings
const locs=(await get(rec,'/api/theatre/locations')).json;const d30=add(t,30);
const s=ok(await post(rec.request,'/api/theatre/sessions',{locationId:locs[0].id,date:d30,startTime:'08:00:00',endTime:'12:30:00',type:'Surgical',surgeonId:hall.id,anaesthetistId:kate.id,notes:null}),'session').json;
const w=await post(rec.request,'/api/theatre/bookings',{sessionId:s.id,patientId:p1,procedure:'Laparoscopic cholecystectomy',laterality:'NotApplicable',durationMinutes:75,anaesthetic:'General',notes:null,acceptWarnings:false});
console.log('booking warnings:',w.status,JSON.stringify(w.json));
ok(await post(rec.request,'/api/theatre/bookings',{sessionId:s.id,patientId:p1,procedure:'Laparoscopic cholecystectomy',laterality:'NotApplicable',durationMinutes:75,anaesthetic:'General',notes:null,acceptWarnings:true}),'book');
ok(await post(rec.request,'/api/waitlist',{patientId:p3,procedure:'Inguinal hernia repair',laterality:'Right',anaesthetic:'General',durationMinutes:60,service:'Surgical',specialistId:hall.id,funder:'HealthNz',priority:'Urgent',addedOn:add(t,-10),notes:null}),'wait');
// ---------- UI: patient record (reception)
const rp=await rec.newPage();watch(rp,'rec');await rp.goto(BASE+'/patients/'+p1);await rp.waitForSelector('h2:has-text("Documents")');
fs.writeFileSync('/tmp/consent.pdf','%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF');
await rp.locator('select').filter({hasText:'Referral'}).first().selectOption('Consent');
await rp.setInputFiles('input[type=file] >> nth=0','/tmp/consent.pdf');await rp.waitForSelector('a:has-text("consent")');
await rp.uncheck('label:has-text("Send by SMS") input');await rp.click('button:has-text("Create link")');await rp.waitForSelector('.copy-box');const link=(await rp.locator('.copy-box code').textContent()).trim();
await rp.click('button:has-text("Correct name, date of birth or NHI")');await rp.locator('label:has-text("Given names") input').fill('Sarah Jane');
await rp.locator('label:has-text("Reason") input').fill('Middle name missed at registration');await rp.locator('label:has-text("Evidence seen") input').fill('Driver licence');
await shot(rp,'01-patient-panels');await rp.click('button:has-text("Send for approval")');await rp.waitForSelector('.ok-text');
// merge request from duplicate
await rp.goto(BASE+'/patients/'+dup);await rp.waitForSelector('h2:has-text("Identity")');await rp.click('button:has-text("Merge duplicate record")');
await rp.fill('input[placeholder^="e.g. ZSC21TN"]','Carpenter');await rp.click('button:has-text("Find")');await rp.click('.picker-results button');
await rp.check('label:has-text("Keep the other record") input');await rp.locator('label:has-text("How did you confirm") input').fill('Same DOB, phone and address; spelling error at registration');
await rp.click('button:has-text("Send for approval")');await rp.waitForSelector('.ok-text');
// transfer pack + protected export
await rp.goto(BASE+'/patients/'+p1+'/transfer');await rp.waitForSelector('h2:has-text("Handover")');
await rp.locator('label:has-text("Reason for transfer") input').fill('Post-op bleeding');await rp.locator('label:has-text("Situation") textarea').fill('Haemodynamically unstable 2h post lap chole');
await rp.click('button:has-text("Record transfer")');await rp.waitForSelector('.ok-text');
await rp.click('button:has-text("Export pack (protected)")');await rp.locator('.export-box input[type=password] >> nth=0').fill('Tasman-Transfer-26');await rp.locator('.export-box input[type=password] >> nth=1').fill('Tasman-Transfer-26');
const [dl]=await Promise.all([rp.waitForEvent('download'),rp.click('.export-box button:has-text("Download")')]);console.log('download',dl.suggestedFilename());await dl.saveAs('/tmp/e5-transfer.zip');
await shot(rp,'02-transfer-pack');
// ---------- privacy officer approves
const pp=await priv.newPage();watch(pp,'priv');await pp.goto(BASE+'/admin/identity');await pp.waitForSelector('h2:has-text("Corrections")');await shot(pp,'03-identity-queue');
await pp.click('button:has-text("Approve") >> nth=0');await pp.waitForTimeout(800);await pp.click('button:has-text("Approve and merge")');await pp.waitForSelector('.ok-text');await shot(pp,'04-identity-done');
// ---------- admin: security & printers, credentialing, offboarding
const ap=await admin.newPage();watch(ap,'admin');await ap.goto(BASE+'/admin/security');await ap.waitForSelector('h2:has-text("Label and wristband printers")');
await ap.click('button:has-text("Add wristband printer")');await ap.locator('.card.subtle label:has-text("Name") input').first().fill('Admissions wristbands');await ap.locator('.card.subtle label:has-text("IP address") input').fill('192.168.1.60');
await ap.click('.card.subtle button:has-text("Save")');await ap.waitForTimeout(600);await shot(ap,'05-security-printers');
await ap.goto(BASE+'/credentialing');await ap.waitForSelector('table');await ap.click('button:has-text("Dr Ben Hall")');await ap.waitForSelector('h3:has-text("Scope")');await shot(ap,'06-credentialing');
await ap.goto(BASE+'/admin/users');await ap.waitForSelector('table');await ap.click('tr:has-text("mere.nurse") button:has-text("Staff leaving")');await ap.waitForSelector('h2:has-text("Staff leaving")');await shot(ap,'07-offboard');
// ---------- specialist portal
const sp=await spec.newPage();watch(sp,'spec');await sp.goto(BASE+'/portal');await sp.waitForSelector('h1:has-text("My TDS patients")');await sp.click('button:has-text("CARPENTER")');await sp.waitForSelector('h3:has-text("Documents")');await shot(sp,'08-specialist-portal');
await sp.click('button:has-text("Waitlist")');await shot(sp,'08b-specialist-waitlist');
const forbidden=await get(spec,'/api/portal/specialist/patients/'+dup);console.log('specialist other patient:',forbidden.status);
console.log('specialist /api/patients:',(await get(spec,'/api/patients?q=a')).status);
// ---------- patient portal
const pub=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:420,height:900}});const mp=await pub.newPage();watch(mp,'portal');
await mp.goto(link);await mp.waitForSelector('input[type=date]');await mp.fill('input[type=date]','1975-03-02');await mp.click('button:has-text("Continue")');await mp.waitForSelector('.error');
await mp.fill('input[type=date]','1975-03-01');await mp.click('button:has-text("Continue")');await mp.waitForSelector('h1:has-text("Kia ora")');
await mp.locator('label:has-text("What do you need?") select').selectOption('ChangeAppointment');await mp.locator('textarea').fill('Can I come a week later please?');await mp.click('button:has-text("Send")');await mp.waitForSelector('.ok-text');
await mp.setInputFiles('input[type=file]','/tmp/consent.pdf');await mp.waitForTimeout(1200);await shot(mp,'09-patient-portal');
const rq=await rec.newPage();watch(rq,'rec2');await rq.goto(BASE+'/portal/requests');await rq.waitForSelector('.mono-box');await shot(rq,'10-portal-requests');
// ---------- roster swaps + payroll
const staffIds={};for(const [n,u,code] of [['Jacqui','jacqui.nurse','E001'],['Mere','mere.nurse','E002']]){const r2=ok(await post(mgr.request,'/api/roster/staff',{name:n,role:'Nurse',userName:u,contractedHours:32,isActive:true,payrollCode:code}),'staff');staffIds[n]=r2.json.id;}
console.log('setup rostering',(await post(admin.request,'/api/admin/modules/rostering/setup/complete',{})).status);
const sh=async(n,d)=>ok(await post(mgr.request,'/api/roster/shifts',{staffId:staffIds[n],date:d,allDay:false,start:'07:30:00',end:'16:00:00',breakMinutes:30,area:'Theatre',isTentative:false,note:null,acceptWarnings:true}),'shift').json.id;
const a1=await sh('Jacqui',add(t,3));const b1=await sh('Mere',add(t,4));
const np=await nurse.newPage();watch(np,'nurse');await np.goto(BASE+'/roster?tab=swaps');await np.waitForSelector('h2:has-text("Offer one of my shifts")');
await np.locator('label:has-text("My shift") select').selectOption(a1);await np.locator('label:has-text("To") select').first().selectOption(staffIds['Mere']);await np.waitForTimeout(500);
await np.locator('label:has-text("In exchange") select').selectOption(b1);await np.click('button:has-text("Ask")');await np.waitForTimeout(800);await shot(np,'11-swap-request');
const swaps=(await get(nurse2,'/api/roster/swaps')).json;ok(await post(nurse2.request,`/api/roster/swaps/${swaps[0].id}/accept`,{}),'accept');
const mgp=await mgr.newPage();watch(mgp,'mgr');await mgp.goto(BASE+'/roster?tab=swaps');await mgp.waitForSelector('button:has-text("Approve")');await mgp.click('button:has-text("Approve")');await mgp.waitForTimeout(800);await shot(mgp,'12-swap-approved');
await mgp.goto(BASE+'/roster?tab=hours');await mgp.waitForSelector('table');await mgp.click('button:has-text("Export timesheet")');await mgp.locator('.export-box input[type=password] >> nth=0').fill('Payroll-Export-26');await mgp.locator('.export-box input[type=password] >> nth=1').fill('Payroll-Export-26');
const [dl2]=await Promise.all([mgp.waitForEvent('download'),mgp.click('.export-box button:has-text("Download")')]);await dl2.saveAs('/tmp/e5-payroll.zip');await shot(mgp,'13-payroll');
// ---------- NBRS
await rq.goto(BASE+'/waitlist/nbrs');await rq.waitForSelector('h2:has-text("Extract")');await rq.locator('label:has-text("From") input').fill(add(t,-30));await rq.locator('label:has-text("To") input').fill(t);await rq.click('button:has-text("Check")');await rq.waitForTimeout(800);await shot(rq,'14-nbrs');

// ---------- 0.10.1: forms rows, booking checks, DYMO labels, 7-Zip link
await post(admin.request,'/api/admin/modules/forms/setup/complete',{});
const f=ok(await post(rec.request,'/api/forms',{patientId:p3,bookingId:null,sendSms:false,sendEmail:false}),'form').json;const tok=f.link.split('/q/')[1];
const pubR=pub.request;ok(await post(pubR,`/api/q/${tok}/open`,{dateOfBirth:'1980-02-02'}),'qopen');
ok(await post(pubR,`/api/q/${tok}/save`,{dateOfBirth:'1980-02-02',answers:{height_cm:'165',weight_kg:'70'}}),'qsave');
ok(await post(pubR,`/api/q/${tok}/save`,{dateOfBirth:'1980-02-02',answers:{height_cm:'166'}}),'qsave2');
const fl=(await get(rec,'/api/forms?view=all&patientId='+p3)).json;console.log('form status',fl[0]?.status);
const bk=(await get(rec,'/api/theatre/patients/'+p1)).json[0];
ok(await rec.request.put(BASE+`/api/theatre/bookings/${bk.id}/details`,{headers:H,data:{isTentative:false,payment:'Insurer',depositNote:null,checklist:['consent','form'],billedExternally:false,theatreIn:null,theatreOut:null,traysNeeded:null,traysUsed:null,loanItems:null,scopeId:null,notes:null}}).then(async r=>({status:r.status(),json:await r.text()})),'details');
ok(await rec.request.put(BASE+`/api/theatre/bookings/${bk.id}/details`,{headers:H,data:{isTentative:false,payment:'Insurer',depositNote:null,checklist:['form','tests'],billedExternally:false,theatreIn:null,theatreOut:null,traysNeeded:null,traysUsed:null,loanItems:null,scopeId:null,notes:null}}).then(async r=>({status:r.status(),json:await r.text()})),'details2');
ok(await post(admin.request,'/api/admin/printers',{name:'Reception DYMO',kind:'Label',language:'Dymo',mediaCode:'99012',host:'',port:0,dpi:300,widthMm:0,lengthMm:0,isDefault:true,isActive:true}),'dymo');
await ap.goto(BASE+'/admin/security');await ap.waitForSelector('text=Download 7-Zip');await shot(ap,'15-security-7zip-dymo');
const lp=await rec.newPage();watch(lp,'labels');await lp.goto(BASE+'/patients/'+p1);await lp.waitForSelector('text=Print labels (DYMO)');await lp.click('text=Print labels (DYMO)');await lp.waitForSelector('.dymo-label');await shot(lp,'16-dymo-labels');

// ---------- 0.10.2: clinicians by id – new list via the form, rename flows through everywhere
const tp=await rec.newPage();watch(tp,'theatre');await tp.goto(BASE+'/theatre?date='+add(t,31));await tp.waitForSelector('h1');
await tp.click('button:has-text("New theatre list")');await tp.locator('label:has-text("Surgeon / endoscopist") select').selectOption({label:'Dr Ben Hall'});
await tp.locator('label:has-text("Anaesthetist") select').selectOption({label:'Dr Kate Ford'});await tp.click('button:has-text("Create list")');await tp.waitForTimeout(1000);
ok(await post(admin.request,'/api/admin/organisation/clinicians',{id:kate.id,name:'Dr Kate Ford-Smith',type:'Anaesthetist',isActive:true}),'rename');
await tp.reload();await tp.waitForSelector('text=Dr Kate Ford-Smith');await shot(tp,'17-renamed-clinician-on-list');
const wl=(await get(rec,'/api/waitlist?view=waiting&specialist='+hall.id)).json;console.log('waitlist by specialist id',wl.length,wl[0]?.specialist);
const sess=(await get(rec,`/api/theatre/sessions?from=${d30}&to=${d30}`)).json;console.log('session anaesthetist now',sess[0]?.anaesthetist);

// ---------- 0.11.0: PDF exports, OCR search, portal auto-SMS, questionnaire + estimate in portal
for(const m of ['reminders','billing']){const r=await post(admin.request,`/api/admin/modules/${m}/setup/complete`,{});console.log('setup',m,r.status);}
await ap.goto(BASE+'/admin/security');await ap.waitForSelector('h2:has-text("Patient portal links")');
await ap.check('label:has-text("Send a portal link by SMS automatically") input');await ap.waitForSelector('.ok-text');
await ap.waitForSelector('h2:has-text("Text recognition")');await shot(ap,'18-portal-autosend-ocr');
const p4=await mk('Tamihana','Rawiri',null,'1969-06-06');
ok(await post(rec.request,'/api/theatre/bookings',{sessionId:s.id,patientId:p4,procedure:'Inguinal hernia repair',laterality:'Left',durationMinutes:60,anaesthetic:'General',notes:null,acceptWarnings:true}),'book p4');
const msgs4=(await get(rec,'/api/reminders/messages?patientId='+p4)).json;console.log('auto portal SMS',JSON.stringify((msgs4||[]).filter(m=>m.kind==='PortalLink').map(m=>[m.channel,m.status])));
// day list PDF (protected)
await tp.goto(BASE+'/theatre?date='+d30);await tp.waitForSelector('h1');await tp.click('button:has-text("Day list PDF")');
await tp.locator('.export-box input[type=password] >> nth=0').fill('Theatre-List-2026');await tp.locator('.export-box input[type=password] >> nth=1').fill('Theatre-List-2026');
const [dl3]=await Promise.all([tp.waitForEvent('download'),tp.click('.export-box button:has-text("Download")')]);await dl3.saveAs('/tmp/e5-daylist.pdf');
const dlb=fs.readFileSync('/tmp/e5-daylist.pdf');console.log('day list pdf',dl3.suggestedFilename(),dlb.slice(0,5).toString(),dlb.includes('/AESV3')?'AES-256':'NOT ENCRYPTED');await shot(tp,'19-daylist-pdf');
// transfer pack as PDF
await rp.goto(BASE+'/patients/'+p1+'/transfer');await rp.waitForSelector('h2:has-text("Handover")');await rp.click('button:has-text("Export pack (protected)")');
await rp.check('.export-box label:has-text("PDF") input');await rp.locator('.export-box input[type=password] >> nth=0').fill('Tasman-Transfer-26');await rp.locator('.export-box input[type=password] >> nth=1').fill('Tasman-Transfer-26');
const [dl4]=await Promise.all([rp.waitForEvent('download'),rp.click('.export-box button:has-text("Download")')]);await dl4.saveAs('/tmp/e5-transfer.pdf');console.log('transfer pdf',dl4.suggestedFilename(),fs.readFileSync('/tmp/e5-transfer.pdf').includes('/AESV3'));
// OCR: upload a scanned image, wait for the background reader, search inside it
await rp.goto(BASE+'/patients/'+p1);await rp.waitForSelector('h2:has-text("Documents")');
await rp.setInputFiles('input[type=file] >> nth=0','/home/claude/repo/TDS-PAS/tests/BrightPAS.Web.Tests/Resources/ocr-sample.png');await rp.waitForSelector('a:has-text("ocr-sample")');
for(let i=0;i<20;i++){await rp.waitForTimeout(4000);await rp.reload();await rp.waitForSelector('h2:has-text("Documents")');if(await rp.locator('.badge:has-text("Searchable")').count()>=1)break;}
await rp.locator('label:has-text("Search inside documents") input').fill('penicillin');await rp.click('button:has-text("Search")');await rp.waitForTimeout(800);
console.log('ocr hits',await rp.locator('text=match').count());await shot(rp,'20-document-search');
// estimate + questionnaire in the patient portal
const est=ok(await post(rec.request,'/api/billing/estimates',{patientId:p1,payer:'SelfFunded',payerName:null,payerReference:null,surgeon:null,anaesthetist:null,notes:null,lines:[{code:null,description:'Facility fee – laparoscopic cholecystectomy',quantity:1,unitPrice:4800},{code:null,description:'Anaesthetist fee',quantity:1,unitPrice:950}],bookingId:bk.id}),'estimate').json;
ok(await post(rec.request,`/api/billing/documents/${est.id}/status`,{status:'Issued',reason:null}),'issue');
const l2=ok(await post(rec.request,`/api/portal/patients/${p1}/link`,{sendSms:true,sendEmail:false}),'link2').json;console.log('manual link sent',JSON.stringify(l2.sent));
await mp.goto(BASE+l2.path);await mp.waitForSelector('input[type=date]');await mp.fill('input[type=date]','1975-03-01');await mp.click('button:has-text("Continue")');await mp.waitForSelector('h1:has-text("Kia ora")');
await mp.waitForSelector('h2:has-text("Your estimate")');await shot(mp,'21-portal-estimate-questionnaire');
await mp.click('button:has-text("I accept this estimate")');await mp.waitForSelector('text=You accepted this estimate');
await mp.click('button:has-text("Start questionnaire")');await mp.waitForURL(/\/q\//);await mp.waitForSelector('input[type=date]');await shot(mp,'22-portal-questionnaire');
console.log('estimate status',(await get(rec,'/api/billing/documents/'+est.id)).json?.status);
console.log('ERRORS',JSON.stringify(errs));await b.close();})().catch(e=>{console.error(e.message.split('\n')[0]);console.log('ERRORS',JSON.stringify(ERR));process.exit(1)});
