import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { useSession } from '../App';
import { Perm } from '../api';

export default function Shell() {
  const { me, signOut, can, mod } = useSession();
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useEffect(() => setOpen(false), [location.pathname]);
  const lic = me?.licence;
  const showLicence = lic && (lic.health !== 'Valid' || lic.developmentKey);

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <button className="menu-btn" aria-expanded={open} aria-controls="mainnav" onClick={() => setOpen(!open)}>
            <span aria-hidden="true">☰</span><span className="sr-only">Menu</span>
          </button>
          <span className="brand-mark">BP</span><span className="brand-name">BrightPAS</span><span className="brand-org">Tasman Day Surgery</span>
        </div>
        <div className="user">
          <span className="user-name">{me?.displayName}</span>
          <span className="user-role">{me?.roles.join(', ')}</span>
          <button className="btn btn-light btn-small" onClick={signOut}>Sign out</button>
        </div>
      </header>
      {showLicence && (
        <div className={`licence-bar ${lic.health === 'Valid' || lic.health === 'ExpiringSoon' ? '' : 'warn'}`} role="status">
          {lic.developmentKey && <strong>Sandbox licence – not for real patient data. </strong>}
          {lic.message}
          {can(Perm.ModulesManage) && <> <Link to="/admin/licence">Licence details</Link></>}
        </div>
      )}
      <div className="body">
        <nav id="mainnav" className={`sidenav ${open ? 'open' : ''}`} aria-label="Main menu">
          <NavLink to="/" end>My work today</NavLink>
          {can(Perm.PatientView) && <NavLink to="/patients" end>Find a patient</NavLink>}
          {can(Perm.PatientRegister) && <NavLink to="/patients/new">Register a patient</NavLink>}
          {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/theatre">Theatre lists</NavLink>}
          {mod('labs') && can(Perm.LabsView) && <NavLink to="/labs">Lab results</NavLink>}
          {mod('forms') && (can(Perm.FormsTriage) || can(Perm.FormsSend)) && <NavLink to="/forms">Pre-admission forms</NavLink>}
          {mod('billing') && can(Perm.BillingView) && <NavLink to="/billing">Estimates &amp; invoices</NavLink>}
          {(can(Perm.UsersManage) || can(Perm.AuditView) || can(Perm.ModulesManage) || can(Perm.Integration)) && <span className="nav-group">Administration</span>}
          {can(Perm.UsersManage) && <NavLink to="/admin/users">User accounts</NavLink>}
          {can(Perm.ModulesManage) && <NavLink to="/admin/licence">Licence &amp; modules</NavLink>}
          {can(Perm.Integration) && <NavLink to="/admin/interfaces">Interfaces</NavLink>}
          {can(Perm.AuditView) && <NavLink to="/audit">Audit log</NavLink>}
        </nav>
        <main className="content" id="main"><Outlet /></main>
      </div>
    </div>
  );
}
