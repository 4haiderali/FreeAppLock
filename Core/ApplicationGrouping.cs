namespace FreeAppLock;

internal sealed record ApplicationProcessSnapshot(string Name, int Pid, string Title, string State,
    string Detail, int WindowCount, bool CanEnforce, string Reason);
internal sealed record ApplicationGroupSnapshot(string Name, int Pid, int[] ProcessIds, string Title,
    string State, string Detail, int WindowCount, bool CanEnforce, string Reason);

internal static class ApplicationGrouping
{
    internal static IEnumerable<ApplicationGroupSnapshot> Build(IEnumerable<ApplicationProcessSnapshot> processes)
    {
        foreach (var group in processes.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var all = group.OrderBy(p => p.Pid).ToArray();
            // Browser renderers/background helpers have no app window. They must not
            // dilute the actual window state or turn a locked browser into "Rule set".
            var windowOwners = all.Where(p => p.WindowCount > 0).ToArray();
            var relevant = windowOwners.Length > 0 ? windowOwners : all;
            var representative = relevant.OrderByDescending(p => p.CanEnforce)
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.Title)).ThenBy(p => p.Pid).First();
            string state;
            if (all.All(p => p.State == "Not locked")) state = "Not locked";
            else if (relevant.Any(p => p.State == "Monitoring off")) state = "Monitoring off";
            else if (relevant.Any(p => p.State == "Not hidden")) state = "Not hidden";
            else if (relevant.Any(p => p.State == "Can't enforce")) state = "Can't enforce";
            else if (windowOwners.Length == 0) state = "Rule set";
            else if (relevant.Any(p => p.State == "Checking")) state = "Checking";
            else if (relevant.Any(p => p.State == "Mixed") ||
                     relevant.Any(p => p.State == "Unlocked") && relevant.Any(p => p.State == "Locked")) state = "Mixed";
            else if (relevant.All(p => p.State == "Unlocked")) state = "Unlocked";
            else state = "Locked";
            int windowCount = all.Sum(p => p.WindowCount);
            var titles = relevant.Select(p => p.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
            string title = representative.Title;
            if (windowCount > 1) title = string.IsNullOrWhiteSpace(title) ? $"{windowCount} windows" : $"{title} · {windowCount} windows";
            bool canEnforce = relevant.Any(p => p.CanEnforce);
            string reason = string.Join(" ", relevant.Where(p => !p.CanEnforce).Select(p => p.Reason).Distinct());
            string detail = $"{all.Length} process(es), {windowCount} discovered window(s). " + (state switch
            {
                "Locked" => "All discovered unauthorized windows are currently minimized; monitoring is reactive.",
                "Mixed" => "Some windows are authorized; other windows remain locked.",
                "Unlocked" => "All discovered windows are authorized.",
                "Rule set" => "A rule exists, but no app window has been discovered.",
                "Not hidden" => "An unauthorized window remains visible.",
                "Can't enforce" => reason,
                "Checking" => "A discovered window is awaiting inspection.",
                "Monitoring off" => "Monitoring is switched off.",
                _ => "No protection rule is set."
            });
            if (titles.Length > 0) detail += "\n" + string.Join("\n", titles);
            if (state != "Can't enforce" && !string.IsNullOrWhiteSpace(reason)) detail += "\n" + reason;
            yield return new ApplicationGroupSnapshot(representative.Name, representative.Pid,
                all.Select(p => p.Pid).Distinct().ToArray(), title, state, detail, windowCount, canEnforce, reason);
        }
    }
}
