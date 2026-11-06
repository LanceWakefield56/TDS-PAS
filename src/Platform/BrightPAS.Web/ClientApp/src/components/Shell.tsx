import { NavLink, Outlet } from 'react-router';
import { useSession } from '../App';
import { Perm } from '../api';

export default function Shell() {
  const { me, signOut, can } = useSession();
  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand"><span className="brand-mark">G</span><span className="brand-name">BrightPAS</span><span className="brand-org">Tasman Day Surgery</span></div>
        <div className="user">
          <span className="user-name">{me?.displayName}</span>
          <span className="user-role">{me?.roles.join(', ')}</span>
          <button className="btn btn-light" onClick={signOut}>Sign out</button>
        </div>
      </header>
      <div className="body">
        <nav className="sidenav" aria-label="Main menu">
          <NavLink to="/" end>My work today</NavLink>
          {can(Perm.PatientView) && <NavLink to="/patients" end>Find a patient</NavLink>}
          {can(Perm.PatientRegister) && <NavLink to="/patients/new">Register a patient</NavLink>}
          <span className="nav-soon" title="Coming in Phase 1">Theatre lists</span>
          <span className="nav-soon" title="Coming in Phase 1">Bookings</span>
          {can(Perm.UsersManage) && <NavLink to="/admin/users">User accounts</NavLink>}
          {can(Perm.AuditView) && <NavLink to="/audit">Audit log</NavLink>}
        </nav>
        <main className="content" id="main"><Outlet /></main>
      </div>
    </div>
  );
}
