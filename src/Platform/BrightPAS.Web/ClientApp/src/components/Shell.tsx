import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { useSession } from '../App';
import { Perm } from '../api';

const SETUP_MODULES = ['theatre', 'referrals', 'labs', 'forms', 'billing', 'reminders', 'letters', 'rostering', 'credentialing', 'portal', 'messaging'];

export default function Shell() {
  const { me, signOut, can, mod } = useSession();
  const [open, setOpen] = useState(false);
  const location = useLocation();
  useEffect(() => setOpen(false), [location.pathname]);
  const lic = me?.licence;
  // Licensed modules that still need their guided set-up (administrators only).
  const setupCount = me && can(Perm.ModulesManage) ? Object.entries(me.modules).filter(([id, a]) => a === 'Full' && !me.moduleSetup?.[id] && SETUP_MODULES.includes(id)).length : 0;
  const showLicence = lic && (lic.health !== 'Valid' || lic.developmentKey || lic.testBuild);

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
      {me?.demo && (
        <div className="demo-bar" role="status"><strong>DEMONSTRATION SYSTEM</strong> – every patient, staff member and number here is invented. Never enter real patient information.</div>
      )}
      {showLicence && (
        <div className={`licence-bar ${lic.health === 'Valid' || lic.health === 'ExpiringSoon' ? '' : 'warn'}`} role="status">
          {lic.testBuild && <strong>Test build – licence keys switched off, all modules available. Not for real patient data. </strong>}
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
          {mod('referrals') && can(Perm.ReferralsView) && <NavLink to="/referrals">Referrals</NavLink>}
          {mod('referrals') && can(Perm.ReferralsView) && <NavLink to="/waitlist">Waitlist</NavLink>}
          {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/calendar">Calendar</NavLink>}
          {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/theatre" end>Theatre lists</NavLink>}
          {mod('labs') && can(Perm.LabsView) && <NavLink to="/labs">Lab results</NavLink>}
          {mod('forms') && (can(Perm.FormsTriage) || can(Perm.FormsSend)) && <NavLink to="/forms">Pre-admission forms</NavLink>}
          {mod('billing') && can(Perm.BillingView) && <NavLink to="/billing">Estimates &amp; invoices</NavLink>}
          {mod('reminders') && can(Perm.RemindersView) && <NavLink to="/reminders">SMS &amp; email</NavLink>}
          {mod('letters') && can(Perm.LettersView) && <NavLink to="/letters" end>Letters</NavLink>}
          {mod('rostering') && can(Perm.RosterView) && <NavLink to="/roster">Roster</NavLink>}
          {mod('portal') && can(Perm.PortalSpecialist) && <NavLink to="/portal" end>My TDS patients</NavLink>}
          {mod('portal') && can(Perm.PortalManage) && <NavLink to="/portal/requests">Portal requests</NavLink>}
          {mod('credentialing') && can(Perm.CredentialingView) && <NavLink to="/credentialing">Credentialing</NavLink>}
          {can(Perm.PatientMerge) && <NavLink to="/admin/identity">Identity checks</NavLink>}
          {(can(Perm.UsersManage) || can(Perm.AuditView) || can(Perm.ModulesManage) || can(Perm.Integration) || can(Perm.OrganisationManage)) && <span className="nav-group">Administration</span>}
          {can(Perm.OrganisationManage) && <NavLink to="/admin/organisation">Organisation set-up</NavLink>}
          {mod('theatre') && can(Perm.TheatreManage) && !can(Perm.TheatreView) && <NavLink to="/theatre/templates">Regular theatre lists</NavLink>}
          {can(Perm.UsersManage) && <NavLink to="/admin/users">User accounts</NavLink>}
          {can(Perm.UsersManage) && <NavLink to="/admin/roles">Roles &amp; permissions</NavLink>}
          {(can(Perm.UsersManage) || can(Perm.OrganisationManage)) && <NavLink to="/admin/security">Security &amp; printers</NavLink>}
          {mod('letters') && can(Perm.LettersTemplates) && <NavLink to="/admin/letters">Letter templates</NavLink>}
          {can(Perm.ModulesManage) && <NavLink to="/admin/licence">Licence &amp; modules{setupCount > 0 && <span className="count">{setupCount}</span>}</NavLink>}
          {mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)) && <NavLink to="/messaging">Messaging &amp; integration</NavLink>}
          {can(Perm.Integration) && <NavLink to="/admin/interfaces">Lab test import</NavLink>}
          {can(Perm.AuditView) && <NavLink to="/audit">Audit log</NavLink>}
        </nav>
        <main className="content" id="main"><Outlet /></main>
      </div>
    </div>
  );
}
