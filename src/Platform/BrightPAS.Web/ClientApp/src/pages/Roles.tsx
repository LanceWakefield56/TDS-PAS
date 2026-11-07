import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, errText } from '../api';

interface Role { name: string; description: string | null; isDefault: boolean; users: number; permissions: string[] }
interface Perm { id: string; group: string; text: string }

/** Roles & permissions: tick what each role may do. Changes apply the next time each user signs in. */
export default function Roles() {
  const [data, setData] = useState<{ roles: Role[]; permissions: Perm[]; adminMustKeep: string[] } | null>(null);
  const [sel, setSel] = useState<string | null>(null);
  const [draft, setDraft] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [newRole, setNewRole] = useState({ name: '', description: '' });
  const load = async (keep?: string) => {
    const d = await api<{ roles: Role[]; permissions: Perm[]; adminMustKeep: string[] }>('/api/admin/roles');
    setData(d);
    const name = keep ?? sel ?? d.roles[0]?.name;
    setSel(name); setDraft(d.roles.find((r) => r.name === name)?.permissions ?? []);
  };
  useEffect(() => { load().catch((e) => setErrors([errText(e)])); }, []);
  if (!data) return errors.length ? <p className="error">{errors[0]}</p> : <p>Loading…</p>;
  const role = data.roles.find((r) => r.name === sel)!;
  const groups = [...new Set(data.permissions.map((p) => p.group))];
  const isAdmin = role?.name === 'System Administrator';
  const changed = role && (draft.length !== role.permissions.length || draft.some((p) => !role.permissions.includes(p)));
  const toggle = (id: string) => setDraft(draft.includes(id) ? draft.filter((p) => p !== id) : [...draft, id]);
  const save = async () => {
    setErrors([]); setMsg(null);
    try { await api('/api/admin/roles', { method: 'PUT', body: { name: role.name, permissions: draft } }); setMsg(`${role.name} saved. Users get the change the next time they sign in.`); await load(role.name); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  const create = async () => {
    setErrors([]);
    try { await api('/api/admin/roles', { body: newRole }); const n = newRole.name; setNewRole({ name: '', description: '' }); await load(n); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><p className="small"><Link to="/admin/users">← User accounts</Link></p><h1>Roles &amp; permissions</h1>
        <p className="muted">Each user gets the permissions of all their roles. Every change is recorded in the audit log.</p></div>
      <div className="split">
        <section className="card">
          <h2>Roles</h2>
          <ul className="role-list">{data.roles.map((r) => (
            <li key={r.name}><button className={r.name === sel ? 'on' : ''} onClick={() => { setSel(r.name); setDraft(r.permissions); setMsg(null); setErrors([]); }}>
              <strong>{r.name}</strong><span className="small muted">{r.users} user{r.users === 1 ? '' : 's'} · {r.permissions.length} permissions{r.isDefault ? '' : ' · custom'}</span></button></li>
          ))}</ul>
          <div className="stack-sm mt">
            <h3>New role</h3>
            <input value={newRole.name} onChange={(e) => setNewRole({ ...newRole, name: e.target.value })} placeholder="e.g. Theatre Coordinator" aria-label="New role name" />
            <input value={newRole.description} onChange={(e) => setNewRole({ ...newRole, description: e.target.value })} placeholder="What it is for" aria-label="Description" />
            <div><button className="btn btn-light btn-small" disabled={!newRole.name.trim()} onClick={create}>Create role</button></div>
          </div>
        </section>
        {role && (
          <section className="card stack">
            <div className="row-between"><h2>{role.name}</h2>{changed && <span className="pill amber">Not saved</span>}</div>
            {role.description && <p className="muted">{role.description}</p>}
            {groups.map((g) => (
              <fieldset key={g} className="perm-group"><legend>{g}</legend>
                {data.permissions.filter((p) => p.group === g).map((p) => {
                  const locked = isAdmin && data.adminMustKeep.includes(p.id);
                  return <label key={p.id} className="check"><input type="checkbox" checked={draft.includes(p.id)} disabled={locked} onChange={() => toggle(p.id)} /> {p.text}{locked && <span className="small muted"> (always on for this role)</span>}</label>;
                })}
              </fieldset>
            ))}
            {errors.length > 0 && <div className="error" role="alert">{errors.map((e) => <div key={e}>{e}</div>)}</div>}
            {msg && <p className="notice" role="status">{msg}</p>}
            <div className="row"><button className="btn btn-primary" disabled={!changed} onClick={save}>Save {role.name}</button>
              {changed && <button className="btn btn-light" onClick={() => setDraft(role.permissions)}>Undo changes</button>}</div>
          </section>
        )}
      </div>
    </div>
  );
}
