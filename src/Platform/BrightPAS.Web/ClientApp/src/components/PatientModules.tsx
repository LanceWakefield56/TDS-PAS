import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, errText, formatDate, money, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import { catClass } from '../pages/FormsList';
import { docStatusClass } from '../pages/Billing';
import { MessageTable } from '../pages/Reminders';

/** The licensed-module panels on a patient's record: bookings, lab results, pre-admission forms, billing. */
export default function PatientModules({ patientId }: { patientId: string }) {
  const { can, mod, writable } = useSession();
  const [bookings, setBookings] = useState<any[] | null>(null);
  const [labs, setLabs] = useState<any[] | null>(null);
  const [forms, setForms] = useState<any[] | null>(null);
  const [docs, setDocs] = useState<any[] | null>(null);
  const [link, setLink] = useState<{ link: string; expiresAt: string; sent?: { channel: string; status: string; lastError: string | null }[] | null } | null>(null);
  const [comms, setComms] = useState<{ preference: any; messages: any[] } | null>(null);
  const [sendBy, setSendBy] = useState({ sms: true, email: false });
  const remindersOn = mod('reminders') && can(Perm.RemindersView);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (mod('theatre') && can(Perm.TheatreView)) api<any[]>(`/api/theatre/patients/${patientId}`).then(setBookings).catch(() => setBookings([]));
    if (mod('labs') && can(Perm.LabsView)) api<any[]>(`/api/labs?view=all&patientId=${patientId}`).then(setLabs).catch(() => setLabs([]));
    if (mod('forms') && can(Perm.FormsSend)) api<any[]>(`/api/forms?view=all&patientId=${patientId}`).then(setForms).catch(() => setForms([]));
    if (mod('billing') && can(Perm.BillingView)) api<any[]>(`/api/billing/documents?patientId=${patientId}`).then(setDocs).catch(() => setDocs([]));
    if (mod('reminders') && can(Perm.RemindersView)) api(`/api/reminders/patients/${patientId}`).then(setComms).catch(() => setComms(null));
  }, [patientId, mod, can]);
  const savePref = async (p: any) => {
    setError(null);
    try { await api(`/api/reminders/patients/${patientId}`, { method: 'PUT', body: { allowSms: p.allowSms, allowEmail: p.allowEmail, language: p.language } }); setComms(await api(`/api/reminders/patients/${patientId}`)); }
    catch (e) { setError(errText(e)); }
  };
  const sendForm = async () => {
    setError(null);
    try {
      const body = { patientId, sendSms: remindersOn && writable('reminders') && sendBy.sms, sendEmail: remindersOn && writable('reminders') && sendBy.email };
      const r = await api<any>('/api/forms', { body }); setLink(r); setForms(await api<any[]>(`/api/forms?view=all&patientId=${patientId}`));
      if (remindersOn) setComms(await api(`/api/reminders/patients/${patientId}`));
    }
    catch (e) { setError(errText(e)); }
  };
  return (
    <>
      {bookings && (
        <section className="card">
          <div className="row-between"><h2>Theatre bookings</h2>{can(Perm.TheatreBook) && writable('theatre') && <Link className="btn btn-light btn-small" to={`/theatre?patient=${patientId}`}>Book a procedure</Link>}</div>
          {bookings.length === 0 ? <p className="muted">No bookings.</p> : <ul className="plain">{bookings.map((b) => (
            <li key={b.id}><Link to={`/theatre?date=${b.date}`}><strong>{formatDate(b.date)}</strong></Link> · {b.procedure}{b.laterality !== 'NotApplicable' ? ` (${b.laterality})` : ''} · {b.locationName}, {b.surgeon} <span className="pill">{b.status}</span></li>
          ))}</ul>}
        </section>
      )}
      {labs && (
        <section className="card">
          <h2>Lab results</h2>
          {labs.length === 0 ? <p className="muted">No results.</p> : <ul className="plain">{labs.slice(0, 10).map((r) => (
            <li key={r.id}><Link to={`/labs/${r.id}`}><strong>{r.testName}</strong></Link> · {nzDateTime(r.collectedAt ?? r.receivedAt)} {r.isAbnormal && <span className="pill red">Abnormal</span>} {r.acknowledgedAt ? <span className="pill green">Acknowledged</span> : <span className="pill blue">New</span>}</li>
          ))}</ul>}
        </section>
      )}
      {forms && (
        <section className="card">
          <div className="row-between"><h2>Pre-admission form</h2>{writable('forms') && <button className="btn btn-light btn-small" onClick={sendForm}>{forms.length ? 'Send a new link' : 'Send pre-admission form'}</button>}</div>
          {remindersOn && writable('forms') && writable('reminders') && (
            <div className="row small"><span className="muted">Send the link by</span>
              <label className="check"><input type="checkbox" checked={sendBy.sms} onChange={(e) => setSendBy({ ...sendBy, sms: e.target.checked })} /> text message</label>
              <label className="check"><input type="checkbox" checked={sendBy.email} onChange={(e) => setSendBy({ ...sendBy, email: e.target.checked })} /> email</label></div>)}
          {error && <p className="error">{error}</p>}
          {link?.sent && link.sent.map((m) => <p key={m.channel} className={m.status === 'Sent' ? 'notice' : 'warn-box'}>{m.channel === 'Sms' ? 'Text message' : 'Email'}: {m.status}{m.lastError ? ` – ${m.lastError}` : ''}</p>)}
          {link && (
            <div className="stack">
              <p className="notice">Send this link to the patient (text or email). It works until {nzDateTime(link.expiresAt)} and needs their date of birth.</p>
              <div className="copy-box"><code>{link.link}</code><button className="btn btn-light btn-small" onClick={() => navigator.clipboard?.writeText(link.link)}>Copy</button></div>
            </div>
          )}
          {forms.length === 0 ? <p className="muted">Not sent yet.</p> : <ul className="plain">{forms.slice(0, 3).map((f) => (
            <li key={f.id}>{f.submittedAt ? <>Submitted {nzDateTime(f.submittedAt)}</> : <>Sent {nzDateTime(f.createdAt)}</>} · <span className="pill">{f.status}</span>{' '}
              {(f.category ?? f.suggested) && <span className={`pill ${catClass(f.category ?? f.suggested)}`}>{f.category ?? `Suggested ${f.suggested}`}</span>}{' '}
              {f.submittedAt && can(Perm.FormsTriage) && <Link to={`/forms/${f.id}`}>Open</Link>}</li>
          ))}</ul>}
        </section>
      )}
      {comms && (
        <section className="card">
          <div className="row-between"><h2>Text messages &amp; email</h2><Link className="btn btn-light btn-small" to="/reminders">All messages</Link></div>
          <div className="row small">
            <span>Mobile: <strong>{comms.preference.mobile ?? 'none'}</strong>{comms.preference.mobileOptedOut && <span className="pill amber">Opted out (STOP)</span>}</span>
            <span>Email: <strong>{comms.preference.email ?? 'none'}</strong>{comms.preference.emailOptedOut && <span className="pill amber">Opted out</span>}</span>
          </div>
          {writable('reminders') && (
            <div className="row small">
              <label className="check"><input type="checkbox" checked={comms.preference.allowSms} onChange={(e) => savePref({ ...comms.preference, allowSms: e.target.checked })} /> Texts allowed</label>
              <label className="check"><input type="checkbox" checked={comms.preference.allowEmail} onChange={(e) => savePref({ ...comms.preference, allowEmail: e.target.checked })} /> Email allowed</label>
              <label>Language<select value={comms.preference.language} onChange={(e) => savePref({ ...comms.preference, language: e.target.value })}><option value="en">English</option><option value="mi">Te reo Māori</option></select></label>
            </div>)}
          {comms.messages.length === 0 ? <p className="muted">No messages yet.</p> : <MessageTable rows={comms.messages.slice(0, 10)} showPatient={false} />}
        </section>
      )}
      {docs && (
        <section className="card">
          <div className="row-between"><h2>Estimates &amp; invoices</h2>{can(Perm.BillingManage) && writable('billing') && <Link className="btn btn-light btn-small" to={`/billing/new?kind=Estimate&patient=${patientId}`}>New estimate</Link>}</div>
          {docs.length === 0 ? <p className="muted">None.</p> : <ul className="plain">{docs.map((d) => (
            <li key={d.id}><Link to={`/billing/${d.id}`}><strong>{d.number ?? `Draft ${d.kind.toLowerCase()}`}</strong></Link> · {money(d.totals.total)} <span className={`pill ${docStatusClass(d.status)}`}>{d.status}</span></li>
          ))}</ul>}
        </section>
      )}
    </>
  );
}
