using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Rostering;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

public class BookingDetailsAndAppointmentTests
{
    [Fact]
    public async Task Booking_details_store_teamup_fields_and_validate_times()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var th1 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var (list, _) = await svc.CreateSessionAsync(new(th1.Id, new DateOnly(2026, 11, 12), new(8, 0), new(12, 0), SessionType.Endoscopy, Doc.Id("Dr Ana Lee"), null, null), "rec");
        var b = (await svc.BookAsync(new(list!.Id, p.Id, "Colonoscopy", Laterality.NotApplicable, 30, AnaestheticType.Sedation, null, false, PaymentType.Insurer, IsTentative: true), "rec")).Booking!;
        Assert.True(b.IsTentative);

        Assert.Contains("after time in", await svc.UpdateDetailsAsync(b.Id, new(false, PaymentType.Insurer, null, null, false, new(9, 0), new(8, 30), null, null, null, null, null), "rec"));
        Assert.Contains("checklist", await svc.UpdateDetailsAsync(b.Id, new(false, PaymentType.Insurer, null, ["made-up"], false, null, null, null, null, null, null, null), "rec"));
        Assert.Null(await svc.UpdateDetailsAsync(b.Id, new(false, PaymentType.Insurer, "$350 shortfall", ["consent", "form"], true, new(8, 5), new(8, 40),
            "Colonoscopy set", "Colonoscopy set", "Loan clip applier", "cf-hq190-2", "Prep sent"), "rec"));
        var row = (await svc.SessionsAsync(new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12))).Single().Bookings.Single();
        Assert.False(row.IsTentative);
        Assert.Equal("CF-HQ190-2", row.ScopeId);
        Assert.Equal(["consent", "form"], row.Checklist);
        Assert.True(row.BilledExternally);
        Assert.Equal(new TimeOnly(8, 40), row.TheatreOut);
        var audit = await s.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.SingleAsync(a => a.Action == "UpdateDetails");
        Assert.Contains("scope", audit.Reason);
    }

    [Fact]
    public async Task Appointments_warn_on_clashes_and_record_no_charge()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<TheatreService>();
        var th1 = await s.ServiceProvider.GetRequiredService<PasDbContext>().Locations.SingleAsync(l => l.Code == "TH1");
        var day = new DateOnly(2026, 11, 12);
        await svc.CreateSessionAsync(new(th1.Id, day, new(8, 0), new(12, 0), SessionType.Surgical, Doc.Id("Dr Ben Hall"), null, null), "rec");
        var (_, errors, _) = await svc.BookAppointmentAsync(new(p.Id, day, new(17, 0), 2, AppointmentKind.Consultation, Guid.Empty, null, false, false, null), "rec");
        Assert.Equal(2, errors.Count);
        var (none, _, warnings) = await svc.BookAppointmentAsync(new(p.Id, day, new(11, 30), 15, AppointmentKind.Consultation, Doc.Id("Dr Ben Hall"), null, false, false, null), "rec");
        Assert.Null(none);
        Assert.Contains(warnings, w => w.Contains("theatre list"));
        var (fu, _, _) = await svc.BookAppointmentAsync(new(p.Id, day, new(17, 0), 15, AppointmentKind.FollowUp, Doc.Id("Dr Ben Hall"), null, NoCharge: true, IsTentative: false, "Wound check"), "rec");
        Assert.NotNull(fu);
        var (_, _, clash) = await svc.BookAppointmentAsync(new(p.Id, day, new(17, 10), 15, AppointmentKind.PhoneCall, Doc.Id("Dr Ben Hall"), null, false, false, null), "rec");
        Assert.Contains(clash, w => w.Contains("already has an appointment"));
        Assert.Contains("reason", await svc.UpdateAppointmentAsync(fu!.Id, AppointmentStatus.Cancelled, null, false, null, "rec"));
        Assert.Null(await svc.UpdateAppointmentAsync(fu.Id, AppointmentStatus.Arrived, null, false, "Wound check", "rec"));
        var list = await svc.AppointmentsAsync(day, day, null);
        Assert.True(list.Single().NoCharge);
        Assert.Equal(new TimeOnly(17, 15), list.Single().EndTime);
    }
}

public class RosterTests
{
    private static readonly DateOnly Mon = new(2026, 11, 9);

    private static async Task<(StaffMember Nurse, StaffMember Admin)> StaffAsync(RosterService svc)
    {
        var (n, _) = await svc.SaveStaffAsync(new(null, "Jacqui", StaffRole.Nurse, "jacqui.nurse", null, 32, null, true), "mgr");
        var (a, _) = await svc.SaveStaffAsync(new(null, "Lance", StaffRole.Administration, null, null, null, null, true), "mgr");
        return (n!, a!);
    }

    [Fact]
    public async Task Staff_get_distinct_colours_and_names_are_unique()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<RosterService>();
        var (n, a) = await StaffAsync(svc);
        Assert.NotEqual(n.Colour, a.Colour);
        var (_, dup) = await svc.SaveStaffAsync(new(null, "Jacqui", StaffRole.Nurse, null, null, 100, null, true), "mgr");
        Assert.Equal(2, dup.Count);
    }

    [Fact]
    public async Task Shifts_reject_overlaps_warn_on_leave_and_overtime_and_count_hours()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<RosterService>();
        var (n, a) = await StaffAsync(svc);
        var ok = await svc.SaveShiftAsync(new(null, n.Id, Mon, false, new(7, 30), new(16, 0), 30, "Theatre", false, null), "mgr");
        Assert.NotNull(ok.Shift);
        Assert.Contains("overlaps", (await svc.SaveShiftAsync(new(null, n.Id, Mon, false, new(15, 0), new(18, 0), 0, null, false, null), "mgr")).Errors[0]);
        Assert.NotNull((await svc.SaveShiftAsync(new(null, a.Id, Mon, true, null, null, 0, "Office", false, null), "mgr")).Shift);
        Assert.Contains("finish after", (await svc.SaveShiftAsync(new(null, n.Id, Mon.AddDays(1), false, new(18, 0), new(8, 0), 0, null, false, null), "mgr")).Errors[0]);

        for (var d = 1; d <= 4; d++) await svc.SaveShiftAsync(new(null, n.Id, Mon.AddDays(d), false, new(7, 0), new(17, 30), 30, "Theatre", d == 4, null, AcceptWarnings: true), "mgr");
        var over = await svc.SaveShiftAsync(new(null, n.Id, Mon.AddDays(5), false, new(8, 0), new(16, 0), 0, null, false, null), "mgr");
        Assert.Null(over.Shift);
        Assert.Contains(over.Warnings, w => w.Contains("contracted 32"));

        var (leave, _) = await svc.RequestLeaveAsync(new(n.Id, Mon.AddDays(7), Mon.AddDays(11), LeaveType.Annual, "Holiday"), "jacqui.nurse");
        Assert.Contains("overlaps", (await svc.RequestLeaveAsync(new(n.Id, Mon.AddDays(10), Mon.AddDays(12), LeaveType.Sick, null), "jacqui.nurse")).Errors[0]);
        Assert.Null((await svc.DecideLeaveAsync(leave!.Id, LeaveStatus.Approved, "mgr")).Error);
        var onLeave = await svc.SaveShiftAsync(new(null, n.Id, Mon.AddDays(8), false, new(8, 0), new(12, 0), 0, null, false, null), "mgr");
        Assert.Contains(onLeave.Warnings, w => w.Contains("approved annual leave"));

        var hours = (await svc.HoursAsync(Mon, Mon.AddDays(6))).Single(x => x.StaffId == n.Id);
        Assert.Equal(8m + 4 * 10m, hours.Rostered);
        Assert.Equal(32m, hours.Contracted);
        var shifts = await svc.ShiftsAsync(Mon, Mon.AddDays(6), null);
        Assert.True(shifts.Single(x => x.Date == Mon.AddDays(4) && x.StaffId == n.Id).IsTentative);
        Assert.True(shifts.Single(x => x.StaffId == a.Id).AllDay);
    }

    [Fact]
    public async Task Regular_shifts_generate_and_skip_leave_and_existing()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<RosterService>();
        var (n, a) = await StaffAsync(svc);
        Assert.Empty(await svc.SaveTemplateAsync(new(null, n.Id, DayOfWeek.Tuesday, false, new(8, 0), new(16, 30), 30, "Theatre", 1, Mon, null, true), "mgr"));
        Assert.Empty(await svc.SaveTemplateAsync(new(null, a.Id, DayOfWeek.Monday, true, null, null, 0, "Office", 1, Mon, null, true), "mgr"));
        var (leave, _) = await svc.RequestLeaveAsync(new(n.Id, Mon.AddDays(7), Mon.AddDays(9), LeaveType.Training, null), "mgr");
        await svc.DecideLeaveAsync(leave!.Id, LeaveStatus.Approved, "mgr");
        var (created, skipped) = await svc.GenerateAsync(Mon, Mon.AddDays(27), "mgr");
        Assert.Equal(3 + 4, created);
        Assert.Single(skipped);
        var (again, skipped2) = await svc.GenerateAsync(Mon, Mon.AddDays(27), "mgr");
        Assert.Equal(0, again);
        Assert.Equal(8, skipped2.Count);
        Assert.Contains("13 weeks", (await svc.GenerateAsync(Mon, Mon.AddDays(200), "mgr")).Skipped[0]);
    }
}
