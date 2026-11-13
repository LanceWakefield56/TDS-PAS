import { Link } from 'react-router';
import { useEffect, useState } from 'react';
import { api, ApiError, errText, formatDate, todayIso } from '../api';
import { useSession } from '../App';

interface User { id: string; userName: string; displayName: string; isActive: boolean; mfaEnabled: boolean; lockedOut: boolean; createdAt: string; lastSignInAt: string | null; roles: string[] }
interface Role { name: string; description: string | null }

export default function AdminUsers() {
  const { me } = useSession();
  const [users, setUsers] = useState<User[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [showAdd, setShowAdd] = useState(false);
  const [form, setForm] = useState({ displayName: '', userName: '', password: '', roles: [] as string[] });
  const [editing, setEditing] = useState<string | null>(null);
  const [editRoles, setEditRoles] = useState<string[]>([]);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [offboard, setOffboard] = useState<User | null>(null);

  const load = async () => { setUsers(await api<User[]>('/api/admin/users')); };
  useEffect(() => { load(); api<Role[]>('/api/admin/users/roles').then(setRoles); }, []);

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setMessage(null);
    try { await fn(); setMessage({ ok: true, text: ok }); await load(); return true; }
    catch (e) { setMessage({ ok: false, text: e instanceof ApiError ? (e.body?.errors ?? ['Something went wrong.']).join(' ') : 'Something went wrong.' }); return false; }
  };

  const toggle = (list: string[], r: string) => list.includes(r) ? list.filter((x) => x !== r) : [...list, r];

  const add = async (e: React.FormEvent) => {
    e.preventDefault();
    if (await run(() => api('/api/admin/users', { body: form }), `Account ${form.userName} created. They will set up their phone app the first time they sign in.`)) {
      setForm({ displayName: '', userName: '', password: '', roles: [] }); setShowAdd(false);
    }
  };

  const action = (u: User, path: string, ok: string, confirmText?: string) => {
    if (confirmText && !window.confirm(confirmText)) return;
    run(() => api(`/api/admin/users/${u.id}/${path}`, { body: {} }), ok);
  };

  const resetPassword = (u: User) => {
    const pw = window.prompt(`New temporary password for ${u.displayName} (at least 12 characters). Give it to them in person.`);
    if (pw) run(() => api(`/api/admin/users/${u.id}/reset-password`, { body: { password: pw } }), `Password reset for ${u.userName}.`);
  };

  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>User accounts</h1><p className="muted">Create staff accounts, set roles, and help people who are locked out or lose their phone. <Link to="/admin/roles">Change what each role can do</Link>.</p></div>
        {!showAdd && <button className="btn btn-primary" onClick={() => setShowAdd(true)}>Add a user</button>}
      </div>
      {message && <p className={message.ok ? 'notice' : 'error'} role="status">{message.text}</p>}

      {showAdd && (
        <form className="card stack" onSubmit={add}>
          <h2>New user</h2>
          <div className="form-grid">
            <label>Full name<input value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} required autoFocus /></label>
            <label>Username<input value={form.userName} onChange={(e) => setForm({ ...form, userName: e.target.value.trim() })} required placeholder="e.g. jane.smith" /></label>
            <label>Temporary password<input type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required minLength={12} autoComplete="new-password" /></label>
          </div>
          <fieldset className="roles">
            <legend>Role(s)</legend>
            {roles.map((r) => (
              <label key={r.name} className="check"><input type="checkbox" checked={form.roles.includes(r.name)} onChange={() => setForm({ ...form, roles: toggle(form.roles, r.name) })} />
                <span><strong>{r.name}</strong>{r.description && <span className="muted small"> – {r.description}</span>}</span></label>
            ))}
          </fieldset>
          <div className="row"><button type="button" className="btn btn-light" onClick={() => setShowAdd(false)}>Cancel</button><button className="btn btn-primary" disabled={form.roles.length === 0}>Create account</button></div>
        </form>
      )}

      {offboard && <Offboard user={offboard} onDone={(t) => { setOffboard(null); if (t) setMessage({ ok: true, text: t }); load(); }} />}
      <section className="card">
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Table (scrolls sideways)"><table className="results compact">
          <thead><tr><th>Name</th><th>Username</th><th>Roles</th><th>Status</th><th>Last sign-in</th><th>Actions</th></tr></thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.id}>
                <td><strong>{u.displayName}</strong></td>
                <td>{u.userName}</td>
                <td>
                  {editing === u.id ? (
                    <div className="stack">
                      {roles.map((r) => <label key={r.name} className="check"><input type="checkbox" checked={editRoles.includes(r.name)} onChange={() => setEditRoles(toggle(editRoles, r.name))} />{r.name}</label>)}
                      <div className="row"><button className="btn btn-light" onClick={() => setEditing(null)}>Cancel</button>
                        <button className="btn btn-primary" onClick={async () => { if (await run(() => api(`/api/admin/users/${u.id}/roles`, { method: 'PUT', body: { roles: editRoles } }), 'Roles updated.')) setEditing(null); }}>Save</button></div>
                    </div>
                  ) : u.roles.join(', ')}
                </td>
                <td>
                  {!u.isActive ? <span className="badge off">Inactive</span> : u.lockedOut ? <span className="badge warn">Locked</span> : <span className="badge ok">Active</span>}{' '}
                  {!u.mfaEnabled && u.isActive && <span className="badge warn" title="Will set up the phone app at next sign-in">MFA not set up</span>}
                </td>
                <td>{u.lastSignInAt ? new Date(u.lastSignInAt).toLocaleString('en-NZ') : <span className="muted">never</span>}</td>
                <td className="actions">
                  {editing !== u.id && <button className="btn btn-link" onClick={() => { setEditing(u.id); setEditRoles(u.roles); }}>Roles</button>}
                  {u.lockedOut && <button className="btn btn-link" onClick={() => action(u, 'unlock', `${u.userName} unlocked.`)}>Unlock</button>}
                  <button className="btn btn-link" onClick={() => resetPassword(u)}>Reset password</button>
                  {u.mfaEnabled && <button className="btn btn-link" onClick={() => action(u, 'reset-mfa', `${u.userName} will set up their phone app again at next sign-in.`, `Reset two-step sign-in for ${u.displayName}? Use this if they lost their phone.`)}>Reset phone app</button>}
                  {u.userName !== me?.userName && <button className="btn btn-link danger" onClick={() => setOffboard(u)}>Staff leaving…</button>}
                  {u.userName !== me?.userName && (u.isActive
                    ? <button className="btn btn-link danger" onClick={() => action(u, 'deactivate', `${u.userName} deactivated.`, `Deactivate ${u.displayName}? They will be signed out and cannot sign in.`)}>Deactivate</button>
                    : <button className="btn btn-link" onClick={() => action(u, 'reactivate', `${u.userName} reactivated.`)}>Reactivate</button>)}
                </td>
              </tr>
            ))}
          </tbody>
        </table></div>
      </section>
    </div>
  );
}

/** Staff leaving: switch off sign-in, roster, clinician entry and portal links in one step (audited). */
function Offboard({ user, onDone }: { user: { id: string; displayName: string }; onDone: (msg: string | null) => void }) {
  const [p, setP] = useState<any>(null);
  const [f, setF] = useState({ deactivateAccount: true, endRoster: true, deactivateClinician: true, removePortalLinks: true, lastDay: todayIso(), reason: '' });
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api(`/api/admin/users/${user.id}/offboard`).then(setP).catch((e) => setError(errText(e))); }, [user.id]);
  const go = async () => {
    setError(null);
    try { const r = await api(`/api/admin/users/${user.id}/offboard`, { body: f }); onDone(`${user.displayName}: ${r.done.join(' ')}`); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  const c = (k: keyof typeof f, t: string) => <label className="check" key={k}><input type="checkbox" checked={f[k] as boolean} onChange={(e) => setF({ ...f, [k]: e.target.checked })} /> {t}</label>;
  return (
    <section className="card stack">
      <h2>Staff leaving – {user.displayName}</h2>
      {!p ? <p>Loading…</p> : <>
        <p className="small">Roles: {p.roles.join(', ') || 'none'} · Roster: {p.staff.length ? p.staff.map((s: any) => s.name).join(', ') : 'not on roster'} · Future shifts: {p.futureShifts.length}
          {p.futureShifts.length > 0 && ` (${p.futureShifts.slice(0, 6).map((s: any) => formatDate(s.date)).join(', ')}${p.futureShifts.length > 6 ? '…' : ''})`} · Clinician entry: {p.clinicians.map((x: any) => x.name).join(', ') || 'none'} · PA links: {p.portalLinks}</p>
        <label>Last working day<input type="date" value={f.lastDay} onChange={(e) => setF({ ...f, lastDay: e.target.value })} /></label>
        {c('deactivateAccount', 'Switch off sign-in now and end all sessions')}
        {c('endRoster', 'Remove shifts after the last day (listed for cover), stop regular shifts, close open swaps/leave, set roster record inactive')}
        {c('deactivateClinician', 'Set their clinician register entry inactive')}
        {c('removePortalLinks', 'Remove PA ↔ specialist portal links')}
        <label>Note (optional)<input value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} placeholder="e.g. Resigned" /></label>
        <p className="hint">Records they created stay as they are (audit trail). Nothing is deleted except future shifts.</p>
      </>}
      {error && <p className="error" role="alert">{error}</p>}
      <div className="row"><button className="btn btn-warn" disabled={!p} onClick={go}>Offboard</button><button className="btn btn-light" onClick={() => onDone(null)}>Cancel</button></div>
    </section>
  );
}
