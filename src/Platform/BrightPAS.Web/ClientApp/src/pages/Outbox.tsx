import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, ApiError, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const KIND: Record<string, string> = { BookingConfirmation: 'Booking confirmation', DayBeforeReminder: 'Day-before reminder', FormLink: 'Questionnaire link', PortalLink: 'Portal link', FollowUpCheckIn: 'Follow-up check-in', Test: 'Message', ReplyConfirmed: 'Reply', ReplyCancelRequested: 'Reply', OptOutConfirmed: 'Opt-out reply' };

/** Everything waiting to go, and what went in the last 7 days – undo, release, send now, or report a wrong recipient (spec 7.19, 7.20). */
export default function Outbox() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'held';
  const [d, setD] = useState<{ messages: any[]; letters: any[] } | null>(null);
  const [settings, setSettings] = useState<any>(null);
  const [act, setAct] = useState<{ id: string; what: 'recall' | 'send-now' | 'wrong-recipient'; letter?: boolean } | null>(null);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => { setD(await api('/api/outbox')); }, []);
  useEffect(() => { void load().catch((e) => setErrors(errs(e))); if (can(Perm.RemindersView)) api('/api/outbox/settings').then(setSettings).catch(() => undefined); const t = window.setInterval(() => void load().catch(() => undefined), 15000); return () => window.clearInterval(t); }, [load, can]);
  const run = async (f: () => Promise<any>, ok: string) => { setErrors([]); setMsg(null); try { const r = await f(); setMsg(typeof r?.message === 'string' ? r.message : ok); setAct(null); setReason(''); await load(); } catch (e) { setErrors(errs(e)); } };
  const doAct = () => {
    if (!act) return;
    const url = act.letter ? `/api/outbox/letters/${act.id}/${act.what}` : `/api/outbox/messages/${act.id}/${act.what}`;
    void run(async () => { const r = await api<any>(url, { body: { reason } }); if (act.what === 'wrong-recipient') return { message: r?.recalled ? 'Stopped before it was sent. A privacy incident was opened.' : r?.number ? `Privacy incident opened; retraction letter ${r.number} drafted.` : 'A correction notice has been sent to the same address and a privacy incident opened for the Privacy Officer.' }; return r; },
      act.what === 'recall' ? 'Stopped – it will not be sent.' : 'Sending now.');
  };
  if (!d) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const held = d.messages.filter((m) => m.status === 'Queued');
  const release = held.filter((m) => m.needsRelease);
  const sent = d.messages.filter((m) => m.status !== 'Queued');
  const TABS: [string, string, number][] = [['held', 'Waiting to go', held.length], ['release', 'Needs a second person', release.length], ['sent', 'Sent (7 days)', sent.length], ['letters', 'Letters', d.letters.length]];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Outbox</h1><p className="muted">Messages wait here before they go, so you can stop them. Report anything sent to the wrong person – BrightPAS sends a correction and tells the Privacy Officer.</p></div>
      <div className="tabs" role="tablist">{TABS.map(([k, t, n]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t} <span className="count neutral">{n}</span></button>)}</div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {act && (
        <div className="card subtle stack" role="dialog" aria-label="Confirm">
          <label>{act.what === 'recall' ? 'Why stop it?' : act.what === 'send-now' ? 'Why must it go now (clinically urgent)?' : 'What happened?'}<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div className="row"><button className="btn btn-warn" disabled={!reason.trim()} onClick={doAct}>{act.what === 'recall' ? 'Stop it' : act.what === 'send-now' ? 'Send now' : 'Report it'}</button><button className="btn btn-light" onClick={() => setAct(null)}>Cancel</button></div>
        </div>
      )}
      {tab !== 'letters' && (
        <section className="card">
          {(tab === 'held' ? held : tab === 'release' ? release : sent).length === 0 ? <p className="muted">Nothing here.</p> : (
            <table className="results">
              <thead><tr><th>To</th><th>Message</th><th>{tab === 'sent' ? 'Sent' : 'Goes'}</th><th>By</th><th></th></tr></thead>
              <tbody>{(tab === 'held' ? held : tab === 'release' ? release : sent).map((m) => (
                <tr key={m.id}>
                  <td>{m.patient ? <Link to={`/patients/${m.patientId}`}>{m.patient}</Link> : '—'}<div className="small muted">{m.channel === 'Sms' ? 'SMS' : 'Email'} {m.address}</div></td>
                  <td><strong>{KIND[m.kind] ?? m.kind}</strong><div className="small muted msg-preview">{m.subject ? `${m.subject} – ` : ''}{m.body}</div></td>
                  <td className="small">{tab === 'sent' ? nzDateTime(m.sentAt) : m.needsRelease ? 'after release' : nzDateTime(m.nextAttemptAt)}{m.wrongRecipientAt && <div className="pill red">wrong recipient</div>}{m.recalledAt && <div className="pill muted">stopped</div>}</td>
                  <td className="small">{m.createdBy}</td>
                  <td><div className="row wrap">
                    {m.status === 'Queued' && <button className="btn btn-light btn-small" onClick={() => setAct({ id: m.id, what: 'recall' })}>Undo send</button>}
                    {m.needsRelease && !m.mine && <button className="btn btn-primary btn-small" onClick={() => void run(() => api(`/api/outbox/messages/${m.id}/release`, { body: {} }), 'Released.')}>Release</button>}
                    {m.status === 'Queued' && can(Perm.RemindersManage) && <button className="btn btn-link btn-small" onClick={() => setAct({ id: m.id, what: 'send-now' })}>Send now</button>}
                    {!m.wrongRecipientAt && <button className="btn btn-link danger btn-small" onClick={() => setAct({ id: m.id, what: 'wrong-recipient' })}>Wrong person</button>}
                  </div></td>
                </tr>))}</tbody>
            </table>
          )}
        </section>
      )}
      {tab === 'letters' && (
        <section className="card">
          {d.letters.length === 0 ? <p className="muted">No letters waiting or sent in the last 7 days.</p> : (
            <table className="results"><thead><tr><th>Letter</th><th>Patient</th><th>To</th><th>Status</th><th></th></tr></thead>
              <tbody>{d.letters.map((l) => <tr key={l.id}><td><Link to={`/letters/${l.id}`}>{l.number}</Link><div className="small muted">{l.subject}</div></td><td>{l.patient}</td><td>{l.recipientName}</td>
                <td>{l.status === 'Final' ? 'Final – not sent' : `Sent ${nzDateTime(l.sentAt)}`}{l.wrongRecipientAt && <div className="pill red">wrong recipient</div>}</td>
                <td>{(l.status === 'Sent' || l.status === 'Acknowledged') && !l.wrongRecipientAt && can(Perm.LettersWrite) && <button className="btn btn-link danger btn-small" onClick={() => setAct({ id: l.id, what: 'wrong-recipient', letter: true })}>Wrong person</button>}</td></tr>)}</tbody></table>
          )}
        </section>
      )}
      {settings && (
        <details className="card"><summary>Outbox settings</summary>
          <div className="stack">
            <div className="form-grid">
              <label>Hold messages sent by staff (seconds)<input type="number" disabled={!can(Perm.RemindersManage)} value={settings.holdSeconds} onChange={(e) => setSettings({ ...settings, holdSeconds: Number(e.target.value) })} /></label>
              <label>Hold bulk reminders (seconds)<input type="number" disabled={!can(Perm.RemindersManage)} value={settings.bulkHoldSeconds} onChange={(e) => setSettings({ ...settings, bulkHoldSeconds: Number(e.target.value) })} /></label>
            </div>
            <fieldset><legend>A second person must release these</legend><div className="row wrap">{settings.kinds.filter((k: string) => !k.startsWith('Reply') && k !== 'OptOutConfirmed').map((k: string) => {
              const on = (settings.reviewKinds ?? '').split(',').includes(k);
              return <label key={k} className="check"><input type="checkbox" disabled={!can(Perm.RemindersManage)} checked={on} onChange={() => { const list = (settings.reviewKinds ?? '').split(',').filter((x: string) => x && x !== k); setSettings({ ...settings, reviewKinds: (on ? list : [...list, k]).join(',') }); }} /> {KIND[k] ?? k}</label>;
            })}</div></fieldset>
            {can(Perm.RemindersManage) && <div><button className="btn btn-primary" onClick={() => void run(() => api('/api/outbox/settings', { method: 'PUT', body: settings }), 'Outbox settings saved.')}>Save outbox settings</button></div>}
          </div>
        </details>
      )}
    </div>
  );
}
