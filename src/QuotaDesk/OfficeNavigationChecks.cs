namespace AIUsageMonitor;

/// <summary>Offline contract checks for fixed-order, bounded office navigation.</summary>
internal static class OfficeNavigationChecks
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        try
        {
            var all = OfficeViewport.PhysicalOrder;
            for (var count = 0; count <= 6; count++)
            {
                var view = new OfficeViewport();
                view.SetEnabled(all.Take(count));
                Require(view.VisibleIds.SequenceEqual(all.Take(Math.Min(3, count))), $"initial visible window for {count}");
                Require(view.HasNavigation == (count > 3), $"navigation visibility for {count}");
                Require(!view.Move(-1), $"previous clamps at start for {count}");
                var expectedMoves = Math.Max(0, count - 3);
                for (var step = 0; step < expectedMoves; step++)
                {
                    Require(view.Move(1), $"next advances once for {count}, step {step}");
                    Require(view.StartIndex == step + 1, $"one seat per next for {count}, step {step}");
                }
                Require(!view.Move(1), $"next clamps at end for {count}");
                for (var step = expectedMoves; step > 0; step--)
                {
                    Require(view.Move(-1), $"previous retreats once for {count}, step {step}");
                    Require(view.StartIndex == step - 1, $"one seat per previous for {count}, step {step}");
                }
            }

            var gaps = new OfficeViewport();
            gaps.SetEnabled(["openrouter", "figma", "codex", "magnific"]);
            Require(gaps.EnabledIds.SequenceEqual(["codex", "magnific", "figma", "openrouter"]), "canonical physical order overrides presentation order");
            Require(gaps.VisibleIds.SequenceEqual(["codex", "magnific", "figma"]), "hidden seat gaps stay physically empty");
            gaps.Move(1);
            Require(gaps.VisibleIds.SequenceEqual(["magnific", "figma", "openrouter"]), "second window shifts by one enabled seat");

            var refresh = new OfficeViewport();
            refresh.SetEnabled(all);
            refresh.Move(1);
            refresh.Move(1);
            Require(refresh.VisibleIds.SequenceEqual(["antigravity", "magnific", "figma"]), "rapid navigation remains bounded");
            refresh.SetEnabled(all);
            Require(refresh.VisibleIds.SequenceEqual(["antigravity", "magnific", "figma"]), "refresh preserves current seat");
            refresh.SetEnabled(["codex", "claude", "magnific", "figma", "openrouter"]);
            Require(refresh.VisibleIds.SequenceEqual(["magnific", "figma", "openrouter"]), "hiding anchor clamps to nearest valid window");
            refresh.SetEnabled(["codex", "claude", "magnific"]);
            Require(refresh.VisibleIds.SequenceEqual(["codex", "claude", "magnific"]), "shrinking to three resets to the only window");
            Require(!refresh.HasNavigation && !refresh.CanPrevious && !refresh.CanNext, "arrows absent at three enabled seats");

            CheckMonitorForm(folder);
            lines.Add("PASS: bounded navigation and physical seat ordering (0–6 enabled)");
        }
        catch (Exception ex)
        {
            lines.Add("FAIL: " + ex.Message);
        }
        File.WriteAllLines(Path.Combine(folder, "office-navigation-checks.txt"), lines);
        return lines.Any(line => line.StartsWith("FAIL:", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static void CheckMonitorForm(string folder)
    {
        var statuses = new[]
        {
            new AgentStatus("codex", "Codex", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("Codex", "offline check")),
            new AgentStatus("claude", "Claude", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("Claude", "offline check")),
            new AgentStatus("antigravity", "Antigravity", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("Antigravity", "offline check")),
            new AgentStatus("magnific", "Magnific", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("Magnific", "offline check")),
            new AgentStatus("figma", "Figma", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("Figma", "offline check")),
            new AgentStatus("openrouter", "OpenRouter", ActivityState.Unknown, false, "", "check", ProviderSnapshot.Unavailable("OpenRouter", "offline check"))
        };
        using var form = new MonitorForm { Settings = new AppSettings { Scale = 0.65f } };
        form.ShowData(statuses);
        Require(form.OfficeSceneAvailable, "embedded office scene is available in the production panel");
        Require(form.Width >= 570 && form.Height < 300, "panoramic panel keeps width while reducing height to about half");
        for (var position = 0; position < 4; position++)
        {
            Require(form.OfficeCardIds.SequenceEqual(form.OfficeVisibleIds), $"scene and cards agree at office position {position}");
            using var image = form.Snapshot(position * 10);
            image.Save(Path.Combine(folder, $"office-position-{position}.png"));
            if (position < 3) Require(form.NavigateOffice(1), $"panel navigates to office position {position + 1}");
        }
        Require(!form.NavigateOffice(1), "panel next arrow clamps at final office position");
        Require(form.NavigateOffice(-1), "panel previous arrow returns one office position");
        form.ShowData(statuses); // refresh while in the middle retains the current seat
        Require(form.OfficeVisibleIds.SequenceEqual(["antigravity", "magnific", "figma"]), "panel refresh preserves its visible seat");

        // Rapid input stops at the endpoint and every card remains tied to the camera window.
        for (var i = 0; i < 12; i++) form.NavigateOffice(1);
        Require(form.OfficeVisibleIds.SequenceEqual(["magnific", "figma", "openrouter"]), "rapid panel navigation clamps at the final three seats");
        Require(form.OfficeCardIds.SequenceEqual(form.OfficeVisibleIds), "rapid navigation keeps card IDs matched to the scene");

        for (var count = 1; count <= 3; count++)
        {
            form.Settings.HiddenProviders = statuses.Skip(count).Select(a => a.Id).ToList();
            form.ShowData(statuses);
            var expected = statuses.Take(count).Select(a => a.Id).ToArray();
            Require(form.OfficeVisibleIds.SequenceEqual(expected), $"panel shows {count} enabled providers");
            Require(!form.OfficeNavigationAvailable, $"panel arrows hidden with {count} enabled providers");
            Require(form.OfficeCardIds.SequenceEqual(expected), $"panel cards match the scene with {count} providers");
            using var image = form.Snapshot(count * 10);
            image.Save(Path.Combine(folder, $"office-enabled-{count}.png"));
        }

        form.Settings.HiddenProviders = [];
        form.ShowData(statuses);
        form.NavigateOffice(1);
        form.Settings.HiddenProviders.Add("antigravity");
        form.ShowData(statuses);
        Require(form.OfficeVisibleIds.SequenceEqual(["claude", "magnific", "figma"]), "hiding a visible seat preserves the current first seat");
        Require(form.OfficeCardIds.SequenceEqual(form.OfficeVisibleIds), "visibility change keeps cards tied to scene IDs");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
