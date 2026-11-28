import { useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, ApiError, errText, nzDateTime } from '../api';
import Modal from '../components/Modal';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const TABS: [string, string][] = [['mine', 'My tasks'], ['overdue', 'Late'], ['all', 'All open'], ['done', 'Finished']];

/** People's tasks (0.35.0): jobs made by message flows or by staff, for everyone with the role. Late tasks move to the escalation role. */
export default function Tasks() {
  const [sp, setSp] = useSearchParams();
  const tab = sp.get('tab') ?? 'mine';
  const [rows, setRows] = useState<any[] | null>(null);
  const [roles, setRoles] = useState<string[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const [add, setAdd] = useState<any>(null);
  const [finish, setFinish] = useState<{ id: string; number: string; done: boolean; note: string } | null>(null);
  const load = useCallback(() => { api<any[]>(`/api/messaging/tasks?scope=${tab}`).then(setRows).catch((e) => setErrors(errs(e))); }, [tab]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { api<string[]>('/api/messaging/tasks/roles').then(setRoles).catch(() => undefined); }, []);
  const run = async (fn: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(''); try { await fn(); setMsg(ok); load(); } catch (e) { setErrors(errs(e)); } };

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Tasks</h1><p className="muted">Jobs for you and your role – from message flows (for example an abnormal result to review) or from colleagues. Late tasks go to the person named to take over.</p>
        <div className="row"><button className="btn btn-primary" onClick={() => setAdd({ title: '', detail: '', role: roles[0] ?? '', dueMinutes: '60', escalateToRole: '', priority: 'Normal' })}>New task</button></div></div>
      <nav className="tabs" aria-label="Task views">{TABS.map(([k, t]) => <button key={k} className={tab === k ? 'active' : ''} aria-pressed={tab === k} onClick={() => setSp({ tab: k })}>{t}</button>)}</nav>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="ok" role="status">{msg}</p>}
      <section className="card">
        {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No tasks here.</p> : (
          <table className="results"><thead><tr><th>Task</th><th>For</th><th>Due</th><th>From</th><th></th></tr></thead>
            <tbody>{rows.map((t) => (
              <tr key={t.id} className={t.overdue ? 'warn' : ''}>
                <td data-label="Task"><strong>{t.title}</strong> <span className="small muted">{t.number}</span>{t.priority === 'Urgent' && <span className="pill red">Urgent</span>}
                  {t.detail && <div className="small">{t.detail}</div>}
                  {t.escalatedAt && <div className="small warn">Late – moved from {t.escalatedFrom} {nzDateTime(t.escalatedAt)}</div>}
                  {t.outcome && <div className="small muted">{t.status === 'Done' ? 'Done' : 'Not needed'}: {t.outcome}</div>}</td>
                <td data-label="For">{t.role}{t.assignedTo && <div className="small">Doing it: {t.assignedTo}</div>}</td>
                <td data-label="Due">{t.dueAt ? nzDateTime(t.dueAt) : '—'}{t.overdue && <div className="small warn">late</div>}</td>
                <td data-label="From" className="small">{t.source === 'Flow' ? <>Message flow{t.messageId && <> · <Link to={`/messaging/messages/${t.messageId}`}>message</Link></>}</> : t.createdBy}
                  {t.patientId && <> · <Link to={`/patients/${t.patientId}`}>patient</Link></>}</td>
                <td>{t.status === 'Open' && <div className="row">
                  {!t.assignedTo && <button className="btn btn-light" onClick={() => void run(() => api(`/api/messaging/tasks/${t.id}/take`, { body: {} }), `You are doing ${t.number}.`)}>I'll do it</button>}
                  <button className="btn btn-primary" onClick={() => setFinish({ id: t.id, number: t.number, done: true, note: '' })}>Done</button>
                  <button className="btn btn-link" onClick={() => setFinish({ id: t.id, number: t.number, done: false, note: '' })}>Not needed</button></div>}</td>
              </tr>))}</tbody></table>)}
      </section>
      {finish && (
        <Modal title={`${finish.done ? 'Finish' : 'Not needed'} – ${finish.number}`} onClose={() => setFinish(null)}>
          <div className="stack">
            <label>{finish.done ? 'What was done (optional)' : 'Why it is not needed'}<input value={finish.note} onChange={(e) => setFinish({ ...finish, note: e.target.value })} /></label>
            <div className="row"><button className="btn btn-primary" onClick={() => { const f = finish; setFinish(null); void run(() => api(`/api/messaging/tasks/${f.id}/${f.done ? 'done' : 'cancel'}`, { body: { note: f.note || null } }), `${f.number} finished.`); }}>Save</button>
              <button className="btn btn-link" onClick={() => setFinish(null)}>Cancel</button></div>
          </div>
        </Modal>)}
      {add && (
        <Modal title="New task" onClose={() => setAdd(null)}>
          <div className="stack">
            <label>What has to be done<input value={add.title} maxLength={200} onChange={(e) => setAdd({ ...add, title: e.target.value })} /></label>
            <label>More detail (optional)<textarea value={add.detail} onChange={(e) => setAdd({ ...add, detail: e.target.value })} /></label>
            <div className="form-grid">
              <label>Who does it<select value={add.role} onChange={(e) => setAdd({ ...add, role: e.target.value })}>{roles.map((r) => <option key={r}>{r}</option>)}</select></label>
              <label>Do it within (minutes)<input type="number" min={1} value={add.dueMinutes} onChange={(e) => setAdd({ ...add, dueMinutes: e.target.value })} /></label>
              <label>If late, give it to<select value={add.escalateToRole} onChange={(e) => setAdd({ ...add, escalateToRole: e.target.value })}><option value="">Nobody</option>{roles.filter((r) => r !== add.role).map((r) => <option key={r}>{r}</option>)}</select></label>
              <label>Priority<select value={add.priority} onChange={(e) => setAdd({ ...add, priority: e.target.value })}><option value="Normal">Normal</option><option value="Urgent">Urgent</option></select></label>
            </div>
            <div className="row"><button className="btn btn-primary" onClick={() => { const a = add; void run(async () => { await api('/api/messaging/tasks', { body: { title: a.title, detail: a.detail || null, role: a.role, dueMinutes: a.dueMinutes ? Number(a.dueMinutes) : null, escalateToRole: a.escalateToRole || null, priority: a.priority } }); setAdd(null); }, 'Task made.'); }}>Make task</button>
              <button className="btn btn-link" onClick={() => setAdd(null)}>Cancel</button></div>
          </div>
        </Modal>)}
    </div>
  );
}
