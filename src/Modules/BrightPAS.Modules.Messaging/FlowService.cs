using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Messaging;

public sealed record NodeEdit(string Key, NodeKind Kind, string? Label, int X, int Y, Guid? InterfaceId, Guid? MappingId, MatchMode Match, IReadOnlyList<FilterEdit>? Conditions);
public sealed record EdgeEdit(string From, string To, EdgeBranch Branch);
public sealed record FlowEdit(Guid? Id, string Name, string? Description, bool IsActive, IReadOnlyList<NodeEdit>? Nodes, IReadOnlyList<EdgeEdit>? Edges);
public sealed record FlowRow(Guid Id, string Name, string? Description, bool IsActive, string? Input, IReadOnlyList<string> Outputs, int Nodes, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record FlowView(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<NodeEdit> Nodes, IReadOnlyList<EdgeEdit> Edges, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <summary>One step of a flow run, shown on the canvas in the test harness.</summary>
public sealed record FlowStep(string NodeKey, string Label, string Kind, string Outcome, string? Detail, string? Format, string? Content);
public sealed record FlowOutput(string NodeKey, Guid InterfaceId, string Interface, string Format, string Content, IReadOnlyList<string> Problems);
public sealed record FlowRunResult(IReadOnlyList<FlowStep> Steps, IReadOnlyList<FlowOutput> Outputs, IReadOnlyList<string> Errors);

/// <summary>Visual message flows: build, check, run live and dry-run in the test harness.</summary>
public sealed class FlowService(MsgDbContext db, MappingService mappings, MessagingService messages, AuditWriter audit, TimeProvider clock)
{
    private sealed record Graph(IReadOnlyList<NodeEdit> Nodes, IReadOnlyList<EdgeEdit> Edges);

    public async Task<IReadOnlyList<FlowRow>> FlowsAsync(CancellationToken ct = default)
    {
        var names = await db.Interfaces.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var nodes = (await db.Nodes.AsNoTracking().ToListAsync(ct)).ToLookup(n => n.FlowId);
        return (await db.Flows.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct)).Select(f =>
        {
            var ns = nodes[f.Id].ToList();
            var input = ns.FirstOrDefault(n => n.Kind == NodeKind.Input)?.InterfaceId;
            return new FlowRow(f.Id, f.Name, f.Description, f.IsActive, input is { } i ? names.GetValueOrDefault(i) : null,
                ns.Where(n => n.Kind == NodeKind.Output && n.InterfaceId != null).Select(n => names.GetValueOrDefault(n.InterfaceId!.Value, "?")).Distinct().ToList(), ns.Count, f.UpdatedAt, f.UpdatedBy);
        }).ToList();
    }

    public async Task<FlowView?> FlowAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.Flows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return null;
        var g = await GraphAsync(id, ct);
        return new(f.Id, f.Name, f.Description, f.IsActive, g.Nodes, g.Edges, f.UpdatedAt, f.UpdatedBy);
    }

    private async Task<Graph> GraphAsync(Guid flowId, CancellationToken ct)
    {
        var nodes = await db.Nodes.AsNoTracking().Where(n => n.FlowId == flowId).ToListAsync(ct);
        var ids = nodes.Select(n => n.Id).ToList();
        var conds = (await db.Conditions.AsNoTracking().Where(c => ids.Contains(c.NodeId)).OrderBy(c => c.Sequence).ToListAsync(ct)).ToLookup(c => c.NodeId);
        var edges = await db.Edges.AsNoTracking().Where(e => e.FlowId == flowId).ToListAsync(ct);
        return new(nodes.OrderBy(n => n.X).Select(n => new NodeEdit(n.Id.ToString(), n.Kind, n.Label, n.X, n.Y, n.InterfaceId, n.MappingId, n.Match,
                conds[n.Id].Select(c => new FilterEdit(c.Source, c.Operator, c.Value)).ToList())).ToList(),
            edges.Select(e => new EdgeEdit(e.FromNodeId.ToString(), e.ToNodeId.ToString(), e.Branch)).ToList());
    }

    /// <summary>Checks the drawing: one input, outputs reachable, branches labelled, no loops, and message formats that fit each step.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(IReadOnlyList<NodeEdit> nodes, IReadOnlyList<EdgeEdit> edges, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var byKey = new Dictionary<string, NodeEdit>();
        foreach (var n in nodes) if (!byKey.TryAdd(n.Key, n)) errors.Add("Two steps have the same key.");
        var inputs = nodes.Where(n => n.Kind == NodeKind.Input).ToList();
        if (inputs.Count != 1) { errors.Add("A flow starts with exactly one input (communication point)."); return errors; }
        if (!nodes.Any(n => n.Kind == NodeKind.Output)) errors.Add("Add at least one output (where the message is sent).");
        var ifaces = await db.Interfaces.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var maps = await db.Mappings.AsNoTracking().ToDictionaryAsync(m => m.Id, ct);
        foreach (var e in edges)
        {
            if (!byKey.TryGetValue(e.From, out var from) || !byKey.ContainsKey(e.To)) { errors.Add("A connection points to a step that is not on the canvas."); continue; }
            if (from.Kind == NodeKind.Output) errors.Add($"{Name(from)}: an output is the end of a path – nothing can follow it.");
            if (from.Kind == NodeKind.Decision && e.Branch == EdgeBranch.Next) errors.Add($"{Name(from)}: label each connection from a decision Yes or No.");
            if (from.Kind != NodeKind.Decision && e.Branch != EdgeBranch.Next) errors.Add($"{Name(from)}: only decisions have Yes/No connections.");
            if (byKey[e.To].Kind == NodeKind.Input) errors.Add("Nothing can connect into the input.");
        }
        foreach (var n in nodes)
        {
            switch (n.Kind)
            {
                case NodeKind.Input:
                    if (n.InterfaceId is not { } ii || !ifaces.TryGetValue(ii, out var inI) || inI.Direction != InterfaceDirection.Inbound || inI.Transport == Transport.Monitored)
                        errors.Add($"{Name(n)}: choose an inbound interface.");
                    break;
                case NodeKind.Output:
                    if (n.InterfaceId is not { } oi || !ifaces.TryGetValue(oi, out var outI) || outI.Direction != InterfaceDirection.Outbound || outI.Transport == Transport.Monitored)
                        errors.Add($"{Name(n)}: choose an outbound interface.");
                    break;
                case NodeKind.Mapper:
                    if (n.MappingId is not { } mi || !maps.ContainsKey(mi)) errors.Add($"{Name(n)}: choose a mapping.");
                    break;
                case NodeKind.Filter:
                case NodeKind.Decision:
                    if ((n.Conditions ?? []).Count == 0) errors.Add($"{Name(n)}: add at least one condition.");
                    break;
            }
            if (n.Kind != NodeKind.Input && edges.All(e => e.To != n.Key)) errors.Add($"{Name(n)} is not connected to anything before it.");
            if (n.Kind != NodeKind.Output && edges.All(e => e.From != n.Key)) errors.Add($"{Name(n)} does not lead anywhere – connect it to the next step.");
        }
        if (errors.Count > 0) return errors;

        // Formats along every path (and loop detection).
        var formatAt = new Dictionary<string, MessageFormat>();
        var visiting = new HashSet<string>();
        void Walk(string key, MessageFormat fmt)
        {
            if (visiting.Contains(key)) { errors.Add("The flow loops back on itself – remove the connection that goes backwards."); return; }
            var n = byKey[key];
            if (formatAt.TryGetValue(key, out var seen) && seen != fmt) { errors.Add($"{Name(n)} is reached with both {seen} and {fmt} messages – put a mapper before it on one path."); return; }
            formatAt[key] = fmt;
            var next = fmt;
            switch (n.Kind)
            {
                case NodeKind.Mapper:
                    var m = maps[n.MappingId!.Value];
                    if (m.SourceFormat != fmt) errors.Add($"{Name(n)}: the mapping {m.Name} reads {m.SourceFormat}, but {fmt} arrives here.");
                    next = m.TargetFormat;
                    break;
                case NodeKind.Output:
                    var o = ifaces[n.InterfaceId!.Value];
                    if (o.Format != fmt) errors.Add($"{Name(n)}: {o.Name} sends {o.Format}, but the message here is {fmt}. Add a mapper before it.");
                    break;
                case NodeKind.Filter:
                case NodeKind.Decision:
                    foreach (var c in n.Conditions ?? [])
                        if (MappingEngine.PathProblem(fmt, c.Source) is { } p) errors.Add($"{Name(n)}: {p}");
                    break;
            }
            visiting.Add(key);
            foreach (var e in edges.Where(e => e.From == key)) Walk(e.To, next);
            visiting.Remove(key);
        }
        var input = inputs[0];
        Walk(input.Key, ifaces[input.InterfaceId!.Value].Format);
        return errors.Distinct().ToList();
    }

    private static string Name(NodeEdit n) => string.IsNullOrWhiteSpace(n.Label) ? n.Kind.ToString() : $"\"{n.Label}\"";

    public async Task<(Flow? Flow, IReadOnlyList<string> Errors)> SaveAsync(FlowEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = (e.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) errors.Add("Enter a flow name (2–100 characters).");
        if (await db.Flows.AnyAsync(f => f.Name == name && f.Id != (e.Id ?? Guid.Empty), ct)) errors.Add($"There is already a flow called {name}.");
        var nodes = e.Nodes ?? [];
        var edges = e.Edges ?? [];
        // A flow can be saved unfinished while it is switched off; it must pass every check to be switched on.
        var check = await CheckAsync(nodes, edges, ct);
        if (e.IsActive) errors.AddRange(check);
        if (nodes.Count > 60) errors.Add("A flow can have up to 60 steps.");
        var f = e.Id is { } id ? await db.Flows.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (e.Id is not null && f is null) errors.Add("Flow not found.");
        if (errors.Count > 0) return (null, errors);
        f ??= db.Flows.Add(new Flow()).Entity;
        f.Name = name; f.Description = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description.Trim(); f.IsActive = e.IsActive;
        f.UpdatedAt = clock.GetUtcNow(); f.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        // Replace the drawing (edges first: they reference nodes).
        await db.Edges.Where(x => x.FlowId == f.Id).ExecuteDeleteAsync(ct);
        var oldNodes = await db.Nodes.Where(x => x.FlowId == f.Id).Select(x => x.Id).ToListAsync(ct);
        await db.Conditions.Where(c => oldNodes.Contains(c.NodeId)).ExecuteDeleteAsync(ct);
        await db.Nodes.Where(x => x.FlowId == f.Id).ExecuteDeleteAsync(ct);
        var keyToId = new Dictionary<string, Guid>();
        foreach (var n in nodes)
        {
            var node = new FlowNode
            {
                FlowId = f.Id, Kind = n.Kind, Label = string.IsNullOrWhiteSpace(n.Label) ? n.Kind.ToString() : n.Label.Trim()[..Math.Min(60, n.Label.Trim().Length)],
                X = Math.Clamp(n.X, 0, 5000), Y = Math.Clamp(n.Y, 0, 5000), Match = n.Match,
                InterfaceId = n.Kind is NodeKind.Input or NodeKind.Output ? n.InterfaceId : null, MappingId = n.Kind == NodeKind.Mapper ? n.MappingId : null,
            };
            keyToId[n.Key] = node.Id;
            db.Nodes.Add(node);
            var seq = 0;
            if (n.Kind is NodeKind.Filter or NodeKind.Decision)
                db.Conditions.AddRange((n.Conditions ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Source)).Select(c => new FlowCondition { NodeId = node.Id, Sequence = ++seq, Source = c.Source.Trim(), Operator = c.Operator, Value = c.Value }));
        }
        await db.SaveChangesAsync(ct);
        db.Edges.AddRange(edges.Where(x => keyToId.ContainsKey(x.From) && keyToId.ContainsKey(x.To))
            .Select(x => new FlowEdge { FlowId = f.Id, FromNodeId = keyToId[x.From], ToNodeId = keyToId[x.To], Branch = x.Branch }));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Flow", f.Id.ToString(), reason: $"{f.Name}: {nodes.Count} step(s), {(f.IsActive ? "on" : "off")}", ct: ct);
        return (f, check.Count > 0 && !f.IsActive ? check : []);
    }

    public async Task<string?> DeleteAsync(Guid id, string user, CancellationToken ct = default)
    {
        var f = await db.Flows.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return "Flow not found.";
        if (f.IsActive) return "Switch the flow off before deleting it.";
        await db.Edges.Where(x => x.FlowId == id).ExecuteDeleteAsync(ct);
        db.Flows.Remove(f);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "Flow", id.ToString(), reason: f.Name, ct: ct);
        return null;
    }

    /// <summary>Runs every active flow that starts at the message's interface; outputs are queued to send.</summary>
    public async Task<int> ProcessAsync(Message inbound, string content, CancellationToken ct = default)
    {
        var flowIds = await db.Nodes.AsNoTracking().Where(n => n.Kind == NodeKind.Input && n.InterfaceId == inbound.InterfaceId).Select(n => n.FlowId).Distinct().ToListAsync(ct);
        var flows = await db.Flows.AsNoTracking().Where(f => flowIds.Contains(f.Id) && f.IsActive).ToListAsync(ct);
        var queued = 0;
        foreach (var f in flows)
        {
            var g = await GraphAsync(f.Id, ct);
            var r = await RunAsync(g, content, useDrafts: false, ct);
            foreach (var s in r.Steps.Where(s => s.Outcome is "Stopped" or "Failed" or "Warning"))
                await messages.AddEventAsync(inbound.Id, s.Outcome == "Failed" ? "FlowFailed" : "Flow", $"{f.Name} › {s.Label}: {s.Detail}", ct);
            foreach (var e in r.Errors) await messages.AddEventAsync(inbound.Id, "FlowFailed", $"{f.Name}: {e}", ct);
            var failedOutputs = r.Steps.Where(s => s.Kind == "Output" && s.Outcome == "Failed").Select(s => s.NodeKey).ToHashSet();
            foreach (var o in r.Outputs)
            {
                var iface = await db.Interfaces.AsNoTracking().SingleAsync(i => i.Id == o.InterfaceId, ct);
                if (!iface.IsActive) { await messages.AddEventAsync(inbound.Id, "Flow", $"{f.Name}: {iface.Name} is switched off – not sent.", ct); continue; }
                if (o.Problems.Count > 0 || failedOutputs.Contains(o.NodeKey)) { await messages.FailedOutputAsync(iface, o.Content, inbound, f.Id, string.Join(" ", o.Problems), ct); continue; }
                await messages.QueueOutputAsync(iface, o.Content, inbound, f.Id, $"Produced by flow {f.Name}.", ct);
                await messages.AddEventAsync(inbound.Id, "Routed", $"{f.Name} → {iface.Name}", ct);
                queued++;
            }
        }
        return queued;
    }

    /// <summary>Test harness: runs a flow (saved or still being drawn) against a sample. Nothing is stored or sent. Uses draft mappings when asked.</summary>
    public async Task<FlowRunResult> TestAsync(FlowEdit e, string sample, bool useDrafts, CancellationToken ct = default)
    {
        var errors = await CheckAsync(e.Nodes ?? [], e.Edges ?? [], ct);
        if (errors.Count > 0) return new([], [], errors);
        return await RunAsync(new Graph(e.Nodes ?? [], e.Edges ?? []), sample, useDrafts, ct);
    }

    private async Task<FlowRunResult> RunAsync(Graph g, string content, bool useDrafts, CancellationToken ct)
    {
        var steps = new List<FlowStep>();
        var outputs = new List<FlowOutput>();
        var errors = new List<string>();
        var byKey = g.Nodes.ToDictionary(n => n.Key);
        var input = g.Nodes.FirstOrDefault(n => n.Kind == NodeKind.Input);
        if (input?.InterfaceId is not { } iid) return new([], [], ["The flow has no input."]);
        var inIface = await db.Interfaces.AsNoTracking().SingleOrDefaultAsync(i => i.Id == iid, ct);
        if (inIface is null) return new([], [], ["The input interface no longer exists."]);
        var problems = MessageDocs.Validate(inIface.Format, content);
        steps.Add(new(input.Key, input.Label ?? "Input", "Input", problems.Count == 0 ? "Passed" : "Warning", problems.Count == 0 ? $"Received on {inIface.Name}" : string.Join(" ", problems), inIface.Format.ToString(), Clip(content)));
        var queue = new Queue<(string Key, string Content, MessageFormat Format)>();
        foreach (var e in g.Edges.Where(e => e.From == input.Key)) queue.Enqueue((e.To, content, inIface.Format));
        var guard = 0;
        while (queue.Count > 0 && guard++ < 500)
        {
            var (key, msg, fmt) = queue.Dequeue();
            var n = byKey[key];
            var label = n.Label ?? n.Kind.ToString();
            switch (n.Kind)
            {
                case NodeKind.Filter:
                case NodeKind.Decision:
                {
                    bool result;
                    string detail;
                    try { (result, detail) = Evaluate(n, msg, fmt); }
                    catch (MappingException ex) { steps.Add(new(key, label, n.Kind.ToString(), "Failed", ex.Message, fmt.ToString(), null)); continue; }
                    if (n.Kind == NodeKind.Filter)
                    {
                        steps.Add(new(key, label, "Filter", result ? "Passed" : "Stopped", detail, fmt.ToString(), null));
                        if (result) foreach (var e in g.Edges.Where(e => e.From == key)) queue.Enqueue((e.To, msg, fmt));
                    }
                    else
                    {
                        steps.Add(new(key, label, "Decision", result ? "Yes" : "No", detail, fmt.ToString(), null));
                        var branch = result ? EdgeBranch.Yes : EdgeBranch.No;
                        foreach (var e in g.Edges.Where(e => e.From == key && e.Branch == branch)) queue.Enqueue((e.To, msg, fmt));
                    }
                    break;
                }
                case NodeKind.Mapper:
                {
                    var live = await MappingForRunAsync(n.MappingId!.Value, useDrafts, ct);
                    if (live is null) { steps.Add(new(key, label, "Mapper", "Failed", "The mapping has no live version yet (approve it first, or tick 'use drafts' to test).", fmt.ToString(), null)); continue; }
                    var res = MappingEngine.Run(live.Mapping.SourceFormat, live.Mapping.TargetFormat, msg, live.Version.StartFrom, live.Version.Template, live.Filters, live.Rules, live.Tables);
                    var ver = $"{live.Mapping.Name} v{live.Version.Number}{(live.Version.Status == VersionStatus.Live ? "" : $" ({live.Version.Status})")}";
                    if (res.FilteredOut) { steps.Add(new(key, label, "Mapper", "Stopped", $"{ver}: {string.Join(" ", res.Steps)}", fmt.ToString(), null)); continue; }
                    if (res.Errors.Count > 0 || res.Output is null)
                    {
                        steps.Add(new(key, label, "Mapper", "Failed", $"{ver}: {string.Join(" ", res.Errors)}", fmt.ToString(), null));
                        foreach (var e in g.Edges.Where(e => e.From == key))
                            if (byKey[e.To].Kind == NodeKind.Output && byKey[e.To].InterfaceId is { } oid)
                                outputs.Add(new(e.To, oid, "", live.Mapping.TargetFormat.ToString(), msg, [$"Mapping failed: {string.Join(" ", res.Errors)}"]));
                        continue;
                    }
                    steps.Add(new(key, label, "Mapper", res.Warnings.Count > 0 ? "Warning" : "Passed", $"{ver}: {res.Steps.Count} rule(s) applied{(res.Warnings.Count > 0 ? ". " + string.Join(" ", res.Warnings) : "")}", live.Mapping.TargetFormat.ToString(), Clip(res.Output)));
                    foreach (var e in g.Edges.Where(e => e.From == key)) queue.Enqueue((e.To, res.Output, live.Mapping.TargetFormat));
                    break;
                }
                case NodeKind.Output:
                {
                    var o = await db.Interfaces.AsNoTracking().SingleOrDefaultAsync(i => i.Id == n.InterfaceId, ct);
                    if (o is null) { steps.Add(new(key, label, "Output", "Failed", "The interface no longer exists.", fmt.ToString(), null)); continue; }
                    var p = MessageDocs.Validate(o.Format, msg);
                    steps.Add(new(key, label, "Output", p.Count == 0 ? (o.IsActive ? "Would send" : "Interface off") : "Failed", p.Count == 0 ? $"To {o.Name}" : string.Join(" ", p), o.Format.ToString(), Clip(msg)));
                    outputs.Add(new(key, o.Id, o.Name, o.Format.ToString(), msg, p));
                    break;
                }
            }
        }
        foreach (var o in outputs.Where(o => o.Interface.Length == 0).ToList())
        {
            var name = (await db.Interfaces.AsNoTracking().SingleOrDefaultAsync(i => i.Id == o.InterfaceId, ct))?.Name ?? "?";
            outputs[outputs.IndexOf(o)] = o with { Interface = name };
            steps.Add(new(o.NodeKey, byKey[o.NodeKey].Label ?? "Output", "Output", "Failed", string.Join(" ", o.Problems), o.Format, null));
        }
        return new(steps, outputs, errors);
    }

    private async Task<LiveMapping?> MappingForRunAsync(Guid mappingId, bool useDrafts, CancellationToken ct)
    {
        if (!useDrafts) return await mappings.LiveAsync(mappingId, ct);
        var latest = await db.Versions.AsNoTracking().Where(v => v.MappingId == mappingId && v.Status != VersionStatus.Retired).OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (latest is null) return null;
        var m = await db.Mappings.AsNoTracking().SingleAsync(x => x.Id == mappingId, ct);
        var rules = await db.Rules.AsNoTracking().Where(r => r.VersionId == latest.Id).OrderBy(r => r.Sequence).ToListAsync(ct);
        var filters = await db.Filters.AsNoTracking().Where(f => f.VersionId == latest.Id).OrderBy(f => f.Sequence).ToListAsync(ct);
        var tables = await mappings.TablesAsync(rules.Where(r => r.TranslationTableId != null).Select(r => r.TranslationTableId!.Value).Distinct().ToList(), ct);
        return new(m, latest, filters, rules, tables);
    }

    private static (bool Result, string Detail) Evaluate(NodeEdit n, string msg, MessageFormat fmt)
    {
        var doc = MessageDocs.Parse(fmt, msg);
        var results = (n.Conditions ?? []).Select(c =>
        {
            var v = doc.Get(c.Source);
            return (Ok: MappingEngine.Test(v, c.Operator, c.Value), Text: $"{c.Source} = \"{MessageDocs.Trim(v, 30)}\" {(MappingEngine.Test(v, c.Operator, c.Value) ? "✓" : "✗")} ({c.Operator} {c.Value})");
        }).ToList();
        var ok = n.Match == MatchMode.All ? results.All(r => r.Ok) : results.Any(r => r.Ok);
        return (ok, string.Join("; ", results.Select(r => r.Text)));
    }

    private static string Clip(string s) => s.Length <= 20000 ? s : s[..20000] + "\n…(shortened)";
}
