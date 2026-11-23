import { Children, ReactNode, useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { useSession } from '../App';
import { Perm } from '../api';

const SETUP_MODULES = ['theatre', 'referrals', 'labs', 'forms', 'billing', 'reminders', 'letters', 'rostering', 'credentialing', 'portal', 'messaging', 'assets', 'emr', 'endoscopy', 'scribe', 'quality', 'devices', 'reporting', 'estimates', 'triage'];

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
          <NavLink to="/" end className="primary-nav">My list</NavLink>
          {can(Perm.PatientView) && <NavLink to="/patients" end className="primary-nav">Patients</NavLink>}
          {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/day" className="primary-nav">Theatre day</NavLink>}
          {can(Perm.PatientRegister) && <NavLink to="/patients/new">Register a patient</NavLink>}

          <Group title="Before surgery" paths={['/referrals', '/waitlist', '/estimates', '/forms', '/triage', '/reminders', '/letters', '/portal', '/calendar']}>
            {mod('referrals') && can(Perm.ReferralsView) && <NavLink to="/referrals">Referrals</NavLink>}
            {mod('referrals') && can(Perm.ReferralsView) && <NavLink to="/waitlist">Waitlist</NavLink>}
            {mod('estimates') && (can(Perm.EstimatesBuild) || can(Perm.EstimatesManage)) && <NavLink to="/estimates" end>Procedure estimates</NavLink>}
            {mod('estimates') && can(Perm.EstimatesBuild) && <NavLink to="/estimates/outbox">Estimate outbox</NavLink>}
            {mod('forms') && (can(Perm.FormsTriage) || can(Perm.FormsSend)) && <NavLink to="/forms">Pre-admission forms</NavLink>}
            {mod('triage') && (can(Perm.TriageView) || can(Perm.TriageAssess) || can(Perm.TriageReview) || can(Perm.TriageRules)) && <NavLink to="/triage" end>Triage</NavLink>}
            {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/calendar">Calendar</NavLink>}
            {mod('reminders') && can(Perm.RemindersView) && <NavLink to="/reminders">SMS &amp; email</NavLink>}
            {mod('letters') && can(Perm.LettersView) && <NavLink to="/letters" end>Letters</NavLink>}
            {mod('portal') && can(Perm.PortalSpecialist) && <NavLink to="/portal" end>My TDS patients</NavLink>}
            {mod('portal') && can(Perm.PortalManage) && <NavLink to="/portal/requests">Portal requests</NavLink>}
          </Group>
          <Group title="Day of surgery" paths={['/theatre', '/emr', '/endoscopy', '/scribe', '/devices', '/cssd']}>
            {mod('theatre') && can(Perm.TheatreView) && <NavLink to="/theatre" end>Theatre lists</NavLink>}
            {mod('emr') && can(Perm.EmrView) && <NavLink to="/emr">Clinical record</NavLink>}
            {mod('endoscopy') && can(Perm.EndoView) && <NavLink to="/endoscopy">Endoscopy</NavLink>}
            {mod('scribe') && (can(Perm.ScribeUse) || can(Perm.ScribeManage) || can(Perm.ScribeApprove)) && <NavLink to="/scribe">Scribe</NavLink>}
            {mod('devices') && (can(Perm.DevicesUse) || can(Perm.DevicesManage)) && <NavLink to="/devices">Monitors &amp; SaferSleep</NavLink>}
            {mod('assets') && can(Perm.AssetsView) && <NavLink to="/cssd">Sterile services (CSSD)</NavLink>}
          </Group>
          <Group title="After surgery" paths={['/labs', '/billing', '/coding']}>
            {mod('labs') && can(Perm.LabsView) && <NavLink to="/labs">Lab results</NavLink>}
            {mod('billing') && can(Perm.BillingView) && <NavLink to="/billing">Invoices &amp; payments</NavLink>}
            {mod('reporting') && (can(Perm.CodingCode) || can(Perm.CodingSubmit) || can(Perm.CodingManage)) && <NavLink to="/coding">Coding &amp; reporting</NavLink>}
          </Group>
          <Group title="Running TDS" paths={['/roster', '/assets', '/stock', '/credentialing', '/quality', '/admin/identity']}>
            {mod('quality') && can(Perm.QualityReport) && <NavLink to="/quality/report">Report an incident</NavLink>}
            {mod('quality') && can(Perm.QualityReport) && <NavLink to="/quality/mine">Policies &amp; my reports</NavLink>}
            {mod('quality') && (can(Perm.QualityView) || can(Perm.QualityManage)) && <NavLink to="/quality" end>Quality</NavLink>}
            {mod('rostering') && can(Perm.RosterView) && <NavLink to="/roster">Roster</NavLink>}
            {mod('assets') && can(Perm.AssetsView) && <NavLink to="/assets">Assets &amp; equipment</NavLink>}
            {mod('assets') && can(Perm.AssetsView) && <NavLink to="/stock">Stock &amp; purchasing</NavLink>}
            {mod('credentialing') && can(Perm.CredentialingView) && <NavLink to="/credentialing">Credentialing</NavLink>}
            {can(Perm.PatientMerge) && <NavLink to="/admin/identity">Identity checks</NavLink>}
          </Group>
          <Group title={<>Administration{setupCount > 0 && <span className="count">{setupCount}</span>}</>} paths={['/admin/organisation', '/admin/users', '/admin/roles', '/admin/security', '/admin/letters', '/admin/licence', '/admin/modules', '/messaging', '/admin/interfaces', '/audit', '/theatre/templates', '/triage/rules', '/estimates/admin']}>
            {can(Perm.OrganisationManage) && <NavLink to="/admin/organisation">Organisation set-up</NavLink>}
            {mod('theatre') && can(Perm.TheatreManage) && <NavLink to="/theatre/templates">Regular theatre lists</NavLink>}
            {mod('triage') && (can(Perm.TriageRules) || can(Perm.TriageApprove)) && <NavLink to="/triage/rules">Triage rules</NavLink>}
            {mod('estimates') && can(Perm.EstimatesManage) && <NavLink to="/estimates/admin">Estimates admin</NavLink>}
            {can(Perm.UsersManage) && <NavLink to="/admin/users">User accounts</NavLink>}
            {can(Perm.UsersManage) && <NavLink to="/admin/roles">Roles &amp; permissions</NavLink>}
            {(can(Perm.UsersManage) || can(Perm.OrganisationManage)) && <NavLink to="/admin/security">Security &amp; printers</NavLink>}
            {mod('letters') && can(Perm.LettersTemplates) && <NavLink to="/admin/letters">Letter templates</NavLink>}
            {can(Perm.ModulesManage) && <NavLink to="/admin/licence">Licence &amp; modules{setupCount > 0 && <span className="count">{setupCount}</span>}</NavLink>}
            {mod('messaging') && (can(Perm.MessagingView) || can(Perm.Integration)) && <NavLink to="/messaging">Messaging &amp; integration</NavLink>}
            {can(Perm.Integration) && <NavLink to="/admin/interfaces">Lab test import</NavLink>}
            {can(Perm.AuditView) && <NavLink to="/audit">Audit log</NavLink>}
          </Group>
        </nav>
        <main className="content" id="main"><Outlet /></main>
      </div>
    </div>
  );
}

/** A menu group that hides itself when empty and opens when you are on one of its pages. */
function Group({ title, paths, children }: { title: ReactNode; paths: string[]; children: ReactNode }) {
  const location = useLocation();
  const here = paths.some((p) => location.pathname === p || location.pathname.startsWith(p + '/'));
  const [open, setOpen] = useState(here);
  useEffect(() => { if (here) setOpen(true); }, [here]);
  if (Children.toArray(children).filter(Boolean).length === 0) return null;
  return (
    <details className="nav-section" open={open} onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)}>
      <summary>{title}</summary>
      {children}
    </details>
  );
}
