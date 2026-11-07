import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

export const msgStatusClass = (s: string) =>
  s === 'Sent' || s === 'Delivered' ? 'green' : s === 'Failed' ? 'red' : s === 'Suppressed' ? 'amber' : s === 'Queued' || s === 'Sending' ? 'blue' : '';
const kindLabel: Record<string, string> = {
  BookingConfirmation: 'Booking confirmation', DayBeforeReminder: 'Day-before reminder', FormLink: 'Questionnaire link',
  ReplyConfirmed: 'Reply: confirmed', ReplyCancelRequested: 'Reply: cannot come', OptOutConfirmed: 'Opt-out confirmation', Test: 'Test',
};
export const kindName = (k: string) => kindLabel[k] ?? k;

export default function Reminders() {
  const { can, writable } = useSession();
  const manage = can(Perm.RemindersManage);
  const [params] = useSearchParams();
  const wanted = params.get('tab');
  const [tab, setTab] = useState<'replies' | 'outbox' | 'templates' | 'settings'>(manage && (wanted === 'settings' || wanted === 'templates') ? wanted : wanted === 'outbox' ? 'outbox' : 'replies');
  const [status, setStatus] = useState<{ problems: string[]; counts: { newReplies: number; failed: number; queued: number } } | null>(null);
  const loadStatus = useCallback(() => api('/api/reminders/status').then(setStatus).catch(() => {}), []);
  useEffect(() => { loadStatus(); }, [loadStatus]);
  const tabs: [typeof tab, string, number?][] = [
    ['replies', 'Replies', status?.counts.newReplies], ['outbox', 'Messages sent', status?.counts.failed],
    ...(manage ? [['templates', 'Message wording'] as [typeof tab, string], ['settings', 'Settings & vendors'] as [typeof tab, string]] : []),
  ];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>SMS &amp; email reminders</h1><p className="muted">Booking confirmations, next-day reminders, questionnaire links and patients' text replies.</p></div>
      {!writable('reminders') && <p className="warn-box">Reminders are read-only because of a licence problem. No messages are being sent.</p>}
      {status && status.problems.length > 0 && <div className="warn-box"><strong>Check before relying on reminders:</strong><ul>{status.problems.map((p) => <li key={p}>{p}</li>)}</ul></div>}
      <section className="card">
        <div className="tabs" role="tablist">{tabs.map(([k, t, n]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{t}{n ? <span className="count">{n}</span> : null}</button>)}</div>
        {tab === 'replies' && <Replies onChange={loadStatus} />}
        {tab === 'outbox' && <Outbox onChange={loadStatus} />}
        {tab === 'templates' && <Templates />}
        {tab === 'settings' && <Settings onChange={loadStatus} />}
      </section>
    </div>
  );
}

function Replies({ onChange }: { onChange: () => void }) {
  const [view, setView] = useState('new');
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(() => api<any[]>(`/api/reminders/replies?view=${view}`).then(setRows).catch((e) => setError(errText(e))), [view]);
  useEffect(() => { load(); }, [load]);
  const done = async (id: string) => {
    const note = window.prompt('What did you do? (optional note)') ?? undefined;
    try { await api(`/api/reminders/replies/${id}/handled`, { body: { note } }); await load(); onChange(); } catch (e) { setError(errText(e)); }
  };
  return (
    <div className="stack">
      <div className="chips">{[['new', 'Needs action'], ['all', 'All replies']].map(([k, t]) => <button key={k} className={`chip ${view === k ? 'on' : ''}`} onClick={() => setView(k)}>{t}</button>)}</div>
      {error && <p className="error">{error}</p>}
      {rows?.length === 0 && <p className="muted">{view === 'new' ? 'No replies need action.' : 'No replies yet.'}</p>}
      {rows && rows.length > 0 && (
        <table className="results compact"><thead><tr><th>Received</th><th>From</th><th>Message</th><th>What happened</th><th></th></tr></thead>
          <tbody>{rows.map((r) => (
            <tr key={r.id}>
              <td data-label="Received" className="time">{nzDateTime(r.receivedAt)}</td>
              <td data-label="From">{r.patientId ? <Link to={`/patients/${r.patientId}`}><strong>{r.patientName}</strong></Link> : <span className="muted">Unknown sender</span>}<div className="muted small">{r.from}</div></td>
              <td data-label="Message">“{r.body}” <span className="pill">{r.meaning}</span></td>
              <td data-label="What happened">{r.actionTaken}{r.note && <div className="muted small">Note: {r.note} ({r.handledBy})</div>}</td>
              <td className="actions">{r.status === 'New' ? <button className="btn btn-light btn-small" onClick={() => done(r.id)}>Mark done</button> : <span className="pill green">Done</span>}</td>
            </tr>))}</tbody></table>
      )}
    </div>
  );
}

export function MessageTable({ rows, onRetry, onCancel, showPatient = true }: { rows: any[]; onRetry?: (id: string) => void; onCancel?: (id: string) => void; showPatient?: boolean }) {
  return (
    <table className="results compact"><thead><tr><th>Created</th>{showPatient && <th>Patient</th>}<th>Message</th><th>To</th><th>Status</th><th></th></tr></thead>
      <tbody>{rows.map((m) => (
        <tr key={m.id}>
          <td data-label="Created" className="time">{nzDateTime(m.createdAt)}</td>
          {showPatient && <td data-label="Patient">{m.patientId !== '00000000-0000-0000-0000-000000000000' ? <Link to={`/patients/${m.patientId}`}>{m.patientName}</Link> : m.patientName}</td>}
          <td data-label="Message"><strong>{kindName(m.kind)}</strong> · {m.channel === 'Sms' ? 'SMS' : 'Email'}{m.appointmentDate && <> · for {formatDate(m.appointmentDate)}</>}
            <details><summary className="small">Show text</summary>{m.subject && <p className="small"><b>{m.subject}</b></p>}<p className="small pre">{m.body}</p></details></td>
          <td data-label="To" className="small">{m.address}</td>
          <td data-label="Status"><span className={`pill ${msgStatusClass(m.status)}`}>{m.status}</span>{m.lastError && <div className="small warn-text">{m.lastError}</div>}
            {m.sentAt && <div className="muted small">Sent {nzDateTime(m.sentAt)}{m.provider ? ` via ${m.provider}` : ''}</div>}</td>
          <td className="actions">
            {onRetry && (m.status === 'Failed' || m.status === 'Suppressed') && <button className="btn-link" onClick={() => onRetry(m.id)}>Retry</button>}
            {onCancel && m.status === 'Queued' && <button className="btn-link danger" onClick={() => onCancel(m.id)}>Cancel</button>}
          </td>
        </tr>))}</tbody></table>
  );
}

function Outbox({ onChange }: { onChange: () => void }) {
  const [filter, setFilter] = useState('');
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(() => api<any[]>(`/api/reminders/messages${filter ? `?status=${filter}` : ''}`).then(setRows).catch((e) => setError(errText(e))), [filter]);
  useEffect(() => { load(); }, [load]);
  const act = async (id: string, what: 'retry' | 'cancel') => {
    setError(null);
    try { await api(`/api/reminders/messages/${id}/${what}`, { body: {} }); await load(); onChange(); } catch (e) { setError(errText(e)); }
  };
  return (
    <div className="stack">
      <div className="chips">{[['', 'All'], ['Queued', 'Waiting'], ['Failed', 'Failed'], ['Suppressed', 'Held (not sent)'], ['Sent', 'Sent']].map(([k, t]) =>
        <button key={k} className={`chip ${filter === k ? 'on' : ''}`} onClick={() => setFilter(k)}>{t}</button>)}</div>
      {error && <p className="error">{error}</p>}
      {rows?.length === 0 && <p className="muted">No messages.</p>}
      {rows && rows.length > 0 && <MessageTable rows={rows} onRetry={(id) => act(id, 'retry')} onCancel={(id) => act(id, 'cancel')} />}
    </div>
  );
}

function Templates() {
  const [data, setData] = useState<{ templates: any[]; placeholders: string[] } | null>(null);
  const [edit, setEdit] = useState<any | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api('/api/reminders/templates').then(setData).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const save = async () => {
    setError(null);
    try { await api('/api/reminders/templates', { method: 'PUT', body: edit }); setEdit(null); await load(); } catch (e) { setError(errText(e)); }
  };
  if (!data) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>;
  return (
    <div className="stack">
      <p className="hint">Keep texts short and free of clinical detail (no procedure names). Placeholders: {data.placeholders.join(' ')}. Te reo Māori versions (language “mi”) are used for patients who prefer te reo – have them written or checked by a fluent speaker before switching them on.</p>
      {error && <p className="error">{error}</p>}
      {edit && (
        <div className="card subtle stack">
          <h3>{kindName(edit.kind)} · {edit.channel === 'Sms' ? 'SMS' : 'Email'} · {edit.language === 'mi' ? 'te reo Māori' : 'English'}</h3>
          {edit.channel === 'Email' && <label>Subject<input value={edit.subject ?? ''} onChange={(e) => setEdit({ ...edit, subject: e.target.value })} /></label>}
          <label>Text<textarea rows={edit.channel === 'Sms' ? 4 : 10} value={edit.body} onChange={(e) => setEdit({ ...edit, body: e.target.value })} /></label>
          {edit.channel === 'Sms' && <p className="hint">{edit.body.length} characters (about {Math.max(1, Math.ceil(edit.body.length / 153))} SMS part{edit.body.length > 160 ? 's' : ''} before placeholders are filled in).</p>}
          <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> In use</label>
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
        </div>
      )}
      <table className="results compact"><thead><tr><th>Message</th><th>Channel</th><th>Language</th><th>Text</th><th></th></tr></thead>
        <tbody>{data.templates.map((t) => (
          <tr key={t.id}><td>{kindName(t.kind)}</td><td>{t.channel === 'Sms' ? 'SMS' : 'Email'}</td><td>{t.language}</td>
            <td className="small">{t.subject && <b>{t.subject}<br /></b>}{t.body.length > 140 ? t.body.slice(0, 140) + '…' : t.body} {!t.isActive && <span className="pill">Off</span>}</td>
            <td className="actions"><button className="btn-link" onClick={() => setEdit({ ...t })}>Edit</button>
              {t.language === 'en' && !data.templates.some((x) => x.kind === t.kind && x.channel === t.channel && x.language === 'mi') &&
                <button className="btn-link" onClick={() => setEdit({ ...t, id: undefined, language: 'mi', isActive: false })}>Add te reo</button>}</td></tr>))}</tbody></table>
    </div>
  );
}

function Settings({ onChange }: { onChange: () => void }) {
  const [s, setS] = useState<any | null>(null);
  const [secrets, setSecrets] = useState({ modicaPassword: '', genericAuthValue: '', smtpPassword: '' });
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const [test, setTest] = useState({ channel: 'Sms', to: '' });
  const [reply, setReply] = useState({ from: '', body: 'YES' });
  const load = () => api('/api/reminders/settings').then(setS).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  if (!s) return error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>;
  const set = (k: string, v: unknown) => setS({ ...s, [k]: v });
  const save = async () => {
    setError(null); setSaved(null);
    try {
      const t = (v: string) => (v.length === 5 ? `${v}:00` : v);
      await api('/api/reminders/settings', { method: 'PUT', body: { ...s, ...secrets, quietStart: t(s.quietStart), quietEnd: t(s.quietEnd) } });
      setSecrets({ modicaPassword: '', genericAuthValue: '', smtpPassword: '' }); setSaved('Settings saved.'); await load(); onChange();
    } catch (e) { setError(errText(e)); }
  };
  const sendTest = async () => {
    setError(null); setSaved(null);
    try { const r = await api<any>('/api/reminders/test', { body: test }); setSaved(`Test ${r.status}${r.lastError ? ': ' + r.lastError : ''}${r.provider ? ` (via ${r.provider})` : ''}.`); }
    catch (e) { setError(errText(e)); }
  };
  const simulate = async () => {
    setError(null); setSaved(null);
    try { const r = await api<any>('/api/reminders/simulate-reply', { body: reply }); setSaved(`Reply understood as ${r.meaning}: ${r.actionTaken}`); onChange(); }
    catch (e) { setError(errText(e)); }
  };
  const rotate = async () => { if (!window.confirm('Make a new secret callback key? You must then update the callback addresses in your SMS vendor’s settings.')) return; await api('/api/reminders/settings/rotate-callback-key', { body: {} }); await load(); };
  const secretField = (label: string, key: keyof typeof secrets, isSet: boolean) => (
    <label>{label}<input type="password" autoComplete="new-password" placeholder={isSet ? 'Saved – type to replace' : 'Not set'} value={secrets[key]} onChange={(e) => setSecrets({ ...secrets, [key]: e.target.value })} /></label>
  );
  return (
    <div className="stack">
      {error && <p className="error">{error}</p>}
      {saved && <p className="notice">{saved}</p>}
      <h3>What to send</h3>
      <div className="form-grid">
        <label className="check"><input type="checkbox" checked={s.sendBookingConfirmation} onChange={(e) => set('sendBookingConfirmation', e.target.checked)} /> Confirmation when a booking is made</label>
        <label className="check"><input type="checkbox" checked={s.sendDayBeforeReminder} onChange={(e) => set('sendDayBeforeReminder', e.target.checked)} /> Reminder the day before</label>
        <label className="check"><input type="checkbox" checked={s.sendEmailCopies} onChange={(e) => set('sendEmailCopies', e.target.checked)} /> Also email patients who have an email address</label>
        <label>Send next-day reminders from (hour, 24-h)<input type="number" min={6} max={19} value={s.dayBeforeHour} onChange={(e) => set('dayBeforeHour', Number(e.target.value))} /></label>
        <label>Arrival time = planned start minus (minutes)<input type="number" min={0} max={240} value={s.arrivalLeadMinutes} onChange={(e) => set('arrivalLeadMinutes', Number(e.target.value))} /></label>
        <label>No automatic messages from<input type="time" value={s.quietStart} onChange={(e) => set('quietStart', e.target.value)} /></label>
        <label>… until<input type="time" value={s.quietEnd} onChange={(e) => set('quietEnd', e.target.value)} /></label>
        <label>Clinic name in messages<input value={s.clinicName} onChange={(e) => set('clinicName', e.target.value)} /></label>
        <label>Clinic phone in messages<input value={s.clinicPhone ?? ''} onChange={(e) => set('clinicPhone', e.target.value)} /></label>
      </div>

      <h3>SMS vendor</h3>
      <label>Send text messages with
        <select value={s.smsProvider} onChange={(e) => set('smsProvider', e.target.value)}>
          <option value="Modica">Modica (recommended – NZ)</option>
          <option value="GenericHttp">Another SMS vendor (HTTP API)</option>
          <option value="TestOutbox">Test mode – record only, nothing is sent</option>
          <option value="Off">Off</option>
        </select></label>
      {s.smsProvider === 'Modica' && (
        <div className="form-grid">
          <label>Modica application name<input value={s.modicaApplication ?? ''} onChange={(e) => set('modicaApplication', e.target.value)} /></label>
          {secretField('Modica password', 'modicaPassword', s.modicaPasswordSet)}
          <label>Sender name (mask, optional)<input value={s.modicaMask ?? ''} onChange={(e) => set('modicaMask', e.target.value)} /></label>
          <label>Modica address<input value={s.modicaBaseUrl} onChange={(e) => set('modicaBaseUrl', e.target.value)} /></label>
        </div>
      )}
      {s.smsProvider === 'GenericHttp' && (
        <div className="form-grid">
          <label className="span-all">Vendor address (https://…)<input value={s.genericUrl ?? ''} onChange={(e) => set('genericUrl', e.target.value)} /></label>
          <label>Authentication header name<input value={s.genericAuthHeader ?? ''} onChange={(e) => set('genericAuthHeader', e.target.value)} /></label>
          {secretField('Authentication header value', 'genericAuthValue', s.genericAuthValueSet)}
          <label className="span-all">JSON body ({'{to}'}, {'{message}'}, {'{reference}'} are filled in)<textarea rows={3} value={s.genericBodyTemplate} onChange={(e) => set('genericBodyTemplate', e.target.value)} /></label>
        </div>
      )}
      {(s.smsProvider === 'Modica' || s.smsProvider === 'GenericHttp') && (
        <div className="card subtle stack">
          <p className="small"><strong>Callback addresses for your SMS vendor</strong> (so replies and delivery receipts reach BrightPAS). They contain a secret key – treat them like passwords. The vendor must be able to reach this server over HTTPS with a public certificate (through the DMZ reverse proxy).</p>
          <div className="copy-box"><code>{s.callbacks.replies}</code><button className="btn btn-light btn-small" onClick={() => navigator.clipboard?.writeText(s.callbacks.replies)}>Copy</button></div>
          <div className="copy-box"><code>{s.callbacks.deliveryReceipts}</code><button className="btn btn-light btn-small" onClick={() => navigator.clipboard?.writeText(s.callbacks.deliveryReceipts)}>Copy</button></div>
          <div><button className="btn btn-light btn-small" onClick={rotate}>Make a new callback key</button></div>
        </div>
      )}

      <h3>Email</h3>
      <label>Send email with
        <select value={s.emailProvider} onChange={(e) => set('emailProvider', e.target.value)}>
          <option value="Smtp">SMTP server (e.g. Microsoft 365 relay)</option>
          <option value="TestOutbox">Test mode – record only, nothing is sent</option>
          <option value="Off">Off</option>
        </select></label>
      {s.emailProvider === 'Smtp' && (
        <div className="form-grid">
          <label>SMTP server<input value={s.smtpHost ?? ''} onChange={(e) => set('smtpHost', e.target.value)} /></label>
          <label>Port<input type="number" value={s.smtpPort} onChange={(e) => set('smtpPort', Number(e.target.value))} /></label>
          <label className="check"><input type="checkbox" checked={s.smtpStartTls} onChange={(e) => set('smtpStartTls', e.target.checked)} /> Encrypt (STARTTLS)</label>
          <label>User name (blank for an IP-allowed relay)<input value={s.smtpUser ?? ''} onChange={(e) => set('smtpUser', e.target.value)} /></label>
          {secretField('Password', 'smtpPassword', s.smtpPasswordSet)}
          <label>From address<input value={s.fromAddress ?? ''} onChange={(e) => set('fromAddress', e.target.value)} /></label>
          <label>From name<input value={s.fromName ?? ''} onChange={(e) => set('fromName', e.target.value)} /></label>
        </div>
      )}
      <div className="row"><button className="btn btn-primary" onClick={save}>Save settings</button><span className="muted small">Last changed {nzDateTime(s.updatedAt)} by {s.updatedBy}</span></div>

      <h3>Try it</h3>
      <div className="row">
        <label>Send a test<select value={test.channel} onChange={(e) => setTest({ ...test, channel: e.target.value })}><option value="Sms">SMS to</option><option value="Email">Email to</option></select></label>
        <label className="grow">Mobile or email<input value={test.to} onChange={(e) => setTest({ ...test, to: e.target.value })} /></label>
        <button className="btn btn-light" onClick={sendTest}>Send test</button>
      </div>
      {s.smsProvider === 'TestOutbox' && (
        <div className="row">
          <label>Pretend a reply from<input placeholder="021 123 4567" value={reply.from} onChange={(e) => setReply({ ...reply, from: e.target.value })} /></label>
          <label>saying<input value={reply.body} onChange={(e) => setReply({ ...reply, body: e.target.value })} /></label>
          <button className="btn btn-light" onClick={simulate}>Simulate reply</button>
        </div>
      )}
    </div>
  );
}
