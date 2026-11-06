import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, Location, Perm } from '../api';
import { useSession } from '../App';

export default function Home() {
  const { me, can } = useSession();
  const [locations, setLocations] = useState<Location[]>([]);
  const [version, setVersion] = useState('');
  useEffect(() => {
    api<Location[]>('/api/organisation/locations').then(setLocations).catch(() => setLocations([]));
    api<{ version: string }>('/health').then((h) => setVersion(h.version)).catch(() => undefined);
  }, []);
  const today = new Date().toLocaleDateString('en-NZ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const hour = new Date().getHours();
  const greeting = hour < 12 ? 'Mōrena' : 'Kia ora';
  const theatres = locations.filter((l) => l.type === 'Theatre');

  return (
    <div className="stack-lg">
      <div className="page-head">
        <h1>{greeting}, {me?.displayName?.split(' ')[0]}</h1>
        <p className="muted">{today}</p>
      </div>
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
        <section className="card">
          <h2>Today's theatres</h2>
          <ul className="plain">
            {theatres.map((t) => (
              <li key={t.id} className="theatre">
                <span className={`dot ${t.isActive ? 'on' : 'off'}`} aria-hidden />
                <strong>{t.name}</strong>
                <span className="muted">{t.isActive ? 'No lists booked yet' : 'Not in use'}</span>
              </li>
            ))}
          </ul>
          <p className="hint">Theatre lists and bookings arrive in Phase 1.</p>
        </section>
        {can(Perm.UsersManage) && (
          <section className="card">
            <h2>Administration</h2>
            <p>Create staff accounts, set roles and help locked-out users. As System Administrator you do not have access to patient records.</p>
            <Link className="btn btn-primary btn-big" to="/admin/users">Manage user accounts</Link>
          </section>
        )}
        <section className="card subtle">
          <h2>About this system</h2>
          <dl className="facts">
            <dt>Signed in as</dt><dd>{me?.userName}</dd>
            <dt>Role(s)</dt><dd>{me?.roles.join(', ')}</dd>
            <dt>Version</dt><dd>{version || '…'}</dd>
          </dl>
          <p className="hint">You will be signed out automatically after 15 minutes without activity.</p>
        </section>
      </div>
    </div>
  );
}
