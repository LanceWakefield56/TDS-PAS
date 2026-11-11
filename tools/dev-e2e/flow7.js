// 0.12.3 demonstration data: sign in through the screens as several demo accounts and visit every main area.
const { chromium } = require('playwright');
const { totp, BASE } = require('./lib');
const KEY = 'BRIGHTPASDEMOKEY', PW = 'Demo-Passphrase-2026';
const shot = (p, n) => p.screenshot({ path: `/tmp/e7-${n}.png`, fullPage: false });
async function signIn(browser, user) {
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 900 } });
  const p = await ctx.newPage();
  const errors = []; p.on('console', m => { if (m.type() === 'error') errors.push(m.text()); }); p.on('pageerror', e => errors.push(e.message));
  await p.goto(BASE + '/signin');
  await p.waitForSelector('.demo-panel');
  if (user === 'admin') { await p.click('.demo-panel summary'); await shot(p, '00-signin'); }
  await p.fill('input[autocomplete=username]', user);
  await p.fill('input[type=password]', PW);
  await p.click('button.btn-primary');
  await p.waitForSelector('.code-input');
  await p.fill('.code-input', totp(KEY));
  await p.click('button.btn-primary');
  await p.waitForSelector('.demo-bar', { timeout: 15000 });
  return { p, ctx, errors };
}
async function visit(p, path, name, wait = 1500) { await p.goto(BASE + path); await p.waitForTimeout(wait); await shot(p, name); const t = await p.innerText('main, .body'); return t; }
(async () => {
  const browser = await chromium.launch();
  const out = {};
  let s = await signIn(browser, 'admin');
  out.home = (await visit(s.p, '/', '01-admin-home')).slice(0, 300);
  out.modules = (await visit(s.p, '/admin/licence', '02-modules')).includes('Needs set-up');
  await visit(s.p, '/admin/users', '03-users');
  await visit(s.p, '/admin/organisation', '04-organisation');
  await visit(s.p, '/credentialing', '08-credentialing');
  out.adminErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'reception');
  out.receptionHome = (await visit(s.p, '/', '10-reception-home')).slice(0, 400);
  await visit(s.p, '/theatre', '11-theatre', 2500);
  await visit(s.p, '/calendar', '12-calendar', 2500);
  await visit(s.p, '/waitlist', '13-waitlist');
  await visit(s.p, '/referrals', '14-referrals');
  await visit(s.p, '/letters', '15-letters');
  await visit(s.p, '/reminders', '16-reminders');
  await visit(s.p, '/portal/requests', '17-portal');
  await s.p.goto(BASE + '/patients'); await s.p.fill('input', 'Wilson'); await s.p.keyboard.press('Enter'); await s.p.waitForTimeout(1500); await shot(s.p, '18-search');
  await s.p.click('text=WILSON'); await s.p.waitForTimeout(2500); await shot(s.p, '19-patient');
  out.receptionErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'finance');
  await visit(s.p, '/billing', '20-billing', 2500);
  out.financeErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'nurse');
  await visit(s.p, '/forms', '21-forms'); await visit(s.p, '/labs', '22-labs');
  out.nurseErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'roster');
  await visit(s.p, '/roster', '23-roster', 2500);
  out.rosterErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'integration');
  out.messaging = (await visit(s.p, '/messaging', '05-messaging', 3000)).slice(0, 600);
  await visit(s.p, '/messaging/flows', '06-flows', 2000);
  await s.p.click('text=Endoscopy images to clinical record').catch(() => {}); await s.p.waitForTimeout(2000); await shot(s.p, '06b-flow');
  await visit(s.p, '/messaging/mappings', '06c-mappings', 2000);
  out.integrationErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'privacy');
  await visit(s.p, '/identity', '07-identity', 2000);
  out.privacyErrors = s.errors; await s.ctx.close();
  s = await signIn(browser, 'hreid');
  out.surgeonHome = (await visit(s.p, '/', '24-surgeon-home')).slice(0, 300);
  out.surgeonErrors = s.errors; await s.ctx.close();
  console.log(JSON.stringify(out, null, 1));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
