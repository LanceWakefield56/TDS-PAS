import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, Location, Perm, todayIso } from '../api';
import { useSession } from '../App';

interface DaySession { id: string; locationName: string; startTime: string; endTime: string; surgeon: string; bookings: { status: string }[] }

export default function Home() {
  const { me, can, mod } = useSession();
  const [locations, setLocations] = useState<Location[]>([]);
  const [sessions, setSessions] = useState<DaySession[] | null>(null);
  const [labCount, setLabCount] = useState<number | null>(null);
  const [formCount, setFormCount] = useState<number | null>(null);
  const [version, setVersion] = useState('');
  const [wait, setWait] = useState<{ toTriage: number; overdue: number; dueIn14Days: number } | null>(null);
  const SETUP = ['theatre', 'referrals', 'labs', 'forms', 'billing', 'reminders', 'letters', 'rostering', 'credentialing', 'portal'];
  const setupNeeded = me && can(Perm.ModulesManage) ? SETUP.filter((id) => me.modules[id] === 'Full' && !me.moduleSetup?.[id]).length : 0;
  const theatreOn = mod('theatre') && can(Perm.TheatreView);
  useEffect(() => {
    api<Location[]>('/api/organisation/locations').then(setLocations).catch(() => setLocations([]));
    api<{ version: string }>('/health').then((h) => setVersion(h.version)).catch(() => undefined);
    if (theatreOn) api<DaySession[]>(`/api/theatre/sessions?from=${todayIso()}`).then(setSessions).catch(() => setSessions([]));
    if (mod('labs') && can(Perm.LabsView)) api<{ unacknowledged: number }>('/api/labs/count').then((r) => setLabCount(r.unacknowledged)).catch(() => undefined);
    if (mod('forms') && can(Perm.FormsTriage)) api<unknown[]>('/api/forms?view=to-triage').then((r) => setFormCount(r.length)).catch(() => undefined);
    if (mod('referrals') && can(Perm.ReferralsView)) api<{ toTriage: number; overdue: number; dueIn14Days: number }>('/api/referrals/summary').then(setWait).catch(() => undefined);
  }, [theatreOn, mod, can]);
  const today = new Date().toLocaleDateString('en-NZ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const greeting = new Date().getHours() < 12 ? 'Mōrena' : 'Kia ora';
  const theatres = locations.filter((l) => l.type === 'Theatre');

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>{greeting}, {me?.displayName?.split(' ')[0]}</h1><p className="muted">{today}</p></div>
      <div className="grid">
        {can(Perm.PatientView) && (
          <section className="card action">
            <h2>Patients</h2>
            <p>Find a patient by NHI, record number or name.</p>
            <div className="row">
              <Link className="btn btn-primary btn-big" to="/patients">Find a patient</Link>
              {can(Perm.PatientRegister) && <Link className="btn btn-light btn-big" to="/patients/new">Register new</Link>}
            </div>
          </section>
        )}
        {(labCount !== null || formCount !== null || wait !== null) && (
          <section className="card action">
            <h2>Waiting for you</h2>
            <ul className="plain">
              {labCount !== null && <li><Link to="/labs"><strong>{labCount}</strong> lab result{labCount === 1 ? '' : 's'} to acknowledge</Link></li>}
              {formCount !== null && <li><Link to="/forms"><strong>{formCount}</strong> pre-admission form{formCount === 1 ? '' : 's'} to triage</Link></li>}
              {wait && (can(Perm.ReferralsTriage) || wait.toTriage > 0) && <li><Link to="/referrals"><strong>{wait.toTriage}</strong> referral{wait.toTriage === 1 ? '' : 's'} to triage</Link></li>}
              {wait && <li><Link to="/waitlist"><strong>{wait.overdue}</strong> waitlist patient{wait.overdue === 1 ? '' : 's'} past target{wait.dueIn14Days ? `, ${wait.dueIn14Days} due within 14 days` : ''}</Link></li>}
            </ul>
          </section>
        )}
        <section className="card">
          <h2>Today's theatres</h2>
          <ul className="plain">
            {theatres.map((t) => {
              const lists = sessions?.filter((s) => s.locationName === t.name) ?? [];
              const cases = lists.reduce((n, s) => n + s.bookings.filter((b) => b.status !== 'Cancelled' && b.status !== 'DidNotAttend').length, 0);
              return (
                <li key={t.id} className="theatre">
                  <span className={`dot ${t.isActive ? 'on' : 'off'}`} aria-hidden />
                  <strong>{t.name}</strong>
                  <span className="muted">{!t.isActive ? 'Not in use' : lists.length === 0 ? 'No lists today' : lists.map((s) => `${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)} ${s.surgeon}`).join('; ') + ` · ${cases} case${cases === 1 ? '' : 's'}`}</span>
                </li>
              );
            })}
          </ul>
          {theatreOn && <Link className="btn btn-light" to="/theatre">Open theatre lists</Link>}
        </section>
        {can(Perm.UsersManage) && (
          <section className="card">
            <h2>Administration</h2>
            <p>Create staff accounts, set roles and help locked-out users. As System Administrator you do not have access to patient records.</p>
            {setupNeeded > 0 && <p className="notice">{setupNeeded} licensed module{setupNeeded === 1 ? ' needs' : 's need'} setting up before staff can use {setupNeeded === 1 ? 'it' : 'them'}. <Link to="/admin/licence">Set up modules</Link></p>}
            <div className="row"><Link className="btn btn-primary" to="/admin/users">Manage user accounts</Link>{can(Perm.ModulesManage) && <Link className="btn btn-light" to="/admin/licence">Licence &amp; modules</Link>}</div>
          </section>
        )}
        <section className="card subtle">
          <h2>About this system</h2>
          <dl className="facts">
            <dt>Signed in as</dt><dd>{me?.userName}</dd>
            <dt>Role(s)</dt><dd>{me?.roles.join(', ')}</dd>
            <dt>Version</dt><dd>BrightPAS {version || '…'}</dd>
            <dt>Licence</dt><dd>{me?.licence.edition ?? 'None'} · {me?.licence.health}</dd>
          </dl>
          <p className="hint">You will be signed out automatically after 15 minutes without activity.</p>
        </section>
      </div>
    </div>
  );
}
