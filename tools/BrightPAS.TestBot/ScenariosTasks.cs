using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// People's tasks and the workflow engine (0.35.0): reception takes a task and finishes it on the Tasks screen; the partner lab's patient-change task
/// that nobody did in time moves to the nurses as urgent; a staff member makes a new task from the screen.
/// </summary>
public sealed partial class Scenarios
{
    private const string TasksArea = "Tasks & workflow";

    private async Task TasksStepsAsync()
    {
        await Step(TasksArea, "Reception takes a task (I'll do it) and marks it done", TaskTakeDoneAsync);
        await Step(TasksArea, "A late task from a message flow moves to the nurses as urgent", TaskEscalatedAsync);
        await Step(TasksArea, "A nurse makes a new task for reception from the Tasks screen", TaskCreateAsync);
    }

    private async Task<(Outcome, string)> TaskTakeDoneAsync()
    {
        var rec = await As("reception");
        var page = rec.Page;
        await page.GotoAsync($"{bot.O.Url}/tasks");
        var row = page.Locator("tr", new() { HasText = "Ring the insurer" }).First;
        await row.GetByRole(AriaRole.Button, new() { Name = "I'll do it" }).ClickAsync();
        await Assertions.Expect(row.GetByText("Doing it:")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        await page.GetByLabel("What was done (optional)").FillAsync("Approval number SX-ROBOT.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(page.GetByText("finished.")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var done = await rec.GetAsync("/api/messaging/tasks?scope=done");
        var t = Find(done.Json, x => (Str(x, "title") ?? "").StartsWith("Ring the insurer", StringComparison.Ordinal));
        return t is not null && Str(t.Value, "status") == "Done" && Str(t.Value, "doneBy") == "reception"
            ? (Outcome.Pass, "Task taken and finished; kept with who did it and what was done.")
            : (Outcome.Fail, $"Done list: {done.Text[..Math.Min(300, done.Text.Length)]}");
    }

    private async Task<(Outcome, string)> TaskEscalatedAsync()
    {
        var nurse = await As("nurse");
        for (var i = 0; i < 20; i++)
        {
            var all = await nurse.GetAsync("/api/messaging/tasks?scope=all");
            var t = Find(all.Json, x => (Str(x, "title") ?? "").StartsWith("Check the details the partner lab sent", StringComparison.Ordinal) && Str(x, "escalatedFrom") == "Reception / Booking");
            if (t is not null)
                return Str(t.Value, "priority") == "Urgent" && Str(t.Value, "role") == "Nurse" && Str(t.Value, "source") == "Flow"
                    ? (Outcome.Pass, $"{Str(t.Value, "number")} from the partner lab flow was late for reception and moved to the nurses as urgent.")
                    : (Outcome.Fail, $"Escalated task looks wrong: role {Str(t.Value, "role")}, priority {Str(t.Value, "priority")}, source {Str(t.Value, "source")}.");
            await Task.Delay(5000);
        }
        return (Outcome.Fail, "No escalated partner-lab task reached the nurses within 100 seconds.");
    }

    private async Task<(Outcome, string)> TaskCreateAsync()
    {
        var nurse = await As("nurse");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/tasks");
        await page.GetByRole(AriaRole.Button, new() { Name = "New task" }).ClickAsync();
        await page.GetByLabel("What has to be done").FillAsync("Robot: confirm tomorrow's first patient is fasting");
        await page.GetByLabel("Who does it").SelectOptionAsync("Reception / Booking");
        await page.GetByRole(AriaRole.Button, new() { Name = "Make task" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Task made.")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var rec = await As("reception");
        var mine = await rec.GetAsync("/api/messaging/tasks?scope=mine");
        return Find(mine.Json, x => Str(x, "title") == "Robot: confirm tomorrow's first patient is fasting" && Str(x, "createdBy") == "nurse") is not null
            ? (Outcome.Pass, "Task made on the screen shows in reception's list.")
            : (Outcome.Fail, $"Reception's list: {mine.Text[..Math.Min(300, mine.Text.Length)]}");
    }
}
