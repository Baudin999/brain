using System.Text;
using System.Text.RegularExpressions;

namespace Brain.Tests;

/// <summary>
/// The stylebook as a gate, over brain's window and a workspace's alike: each named component is drawn once, in
/// components.css or in its own sheet in web/components, and every other sheet of either window only places one. A
/// component's own sheet draws only its own classes: the toggle's sheet draws <c>.toggle</c> and <c>.toggle-knob</c>,
/// and no class starts a rule in two component sheets. A component's class is one that starts a rule in a component
/// sheet, or one a script sets beside such a class, as
/// <c>class: 'row queue-item'</c> makes <c>.queue-item</c> a row. A rule of another sheet whose subject carries such a
/// class, or whose classless subject sits inside one, sets only where the component sits: its margin, flex, grid,
/// position, order, size and display. Its color, background, border, radius, font, padding and shadow are the
/// component's own. Every control the window makes, a link, a button, a field, a summary, an element in a control's
/// role, or one the keys reach by its tabindex or a click acts on by its onclick, is a component: each class list a
/// script or the page gives one names a component, and a rule whose subject is a control's tag or a control's id only
/// places it too. A script makes a control through h() or as a tag of a lit-html template, read alike: a template's
/// class attribute is one list of its text and what its interpolations add, and an @click acts as an onclick does. A
/// class a script sets through a variable, not a literal, is not read.
/// </summary>
public sealed partial class ComponentPlacementTests
{
    private static readonly HashSet<string> Sample = ["button", "row"];

    private static readonly HashSet<string> ControlTags = ["a", "button", "input", "select", "summary", "textarea"];

    private static readonly HashSet<string> ControlRoles = ["button", "checkbox", "link", "menuitem", "option", "radio", "switch", "tab"];

    [Fact]
    public void EverySheetButTheComponentsPlacesAComponentAndNeverRestylesIt()
    {
        var roots = WindowRoots();
        var scripts = WindowResources.Named(".js").Select(WindowResources.Read).ToList();
        var drawn = roots.Concat(scripts.SelectMany((js) => CoClasses(js, roots))).ToHashSet();
        var ids = WindowControls().SelectMany((control) => control.Ids).ToHashSet();
        var sheets = WindowResources.Named(".css").Except(WindowResources.ComponentSheets()).ToList();

        var problems = sheets.SelectMany((name) => RestyleProblems(name, WindowResources.Read(name), drawn, ids)).ToList();

        Assert.True(sheets.Count >= 5, $"only {sheets.Count} sheets were read");
        Assert.Contains("queue-item", drawn);
        Assert.Contains("toggle", drawn);
        Assert.Contains("settings.css", string.Join(' ', sheets), StringComparison.Ordinal);
        Assert.Contains("comment-pop", ids);
        Assert.Contains("palette-input", ids);
        Assert.Contains("ws-bar-item", drawn);
        Assert.Contains("bar-input", ids);
        Assert.Contains("workspace/workspace.css", sheets);
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void EveryControlTheWindowMakesIsAComponent()
    {
        var roots = WindowRoots();
        var controls = WindowControls().ToList();

        var problems = controls.SelectMany((control) => UndrawnProblems(control, roots)).ToList();

        Assert.True(controls.Count >= 50, $"only {controls.Count} controls were read");
        Assert.Contains(controls, (control) => control.Where.StartsWith("shared/components/toggle.js:", StringComparison.Ordinal));
        Assert.Contains(controls, (control) => control.Where.StartsWith("workspace/tree.js:", StringComparison.Ordinal));
        Assert.Contains(controls, (control) => control.Where.StartsWith("workspace/git.js:", StringComparison.Ordinal));
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void TheComponentSheetsDrawEachNamedComponent()
    {
        var roots = ComponentRoots(WindowResources.Read(WindowResources.Components));

        foreach (var component in new[] { "button", "chip", "segmented", "row", "pane-head", "pane-title", "icon", "link", "fold", "field", "tree-indent", "tree-twisty", "palette", "palette-box", "palette-list", "git-change", "git-gap", "git-run", "git-file-state", "git-file-path", "git-commit", "git-commit-hash", "git-commit-subject", "agent-row-kind", "agent-message", "trail-step", "navarone-signal", "verdict" })
        {
            Assert.Contains(component, roots);
        }

        foreach (var component in new[] { "select", "option-list", "model-selector", "embedder-picker" })
        {
            Assert.Contains(component, ComponentRoots(WindowResources.Read(WindowResources.ComponentFolder + component + ".css")));
            Assert.DoesNotContain(component, roots);
        }

        foreach (var component in new[] { "toggle", "field-row" })
        {
            Assert.Contains(component, ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + component + ".css")));
            Assert.DoesNotContain(component, roots);
        }

        Assert.Contains("image-viewer", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "image-viewer.css")));
        Assert.DoesNotContain("image-viewer", roots);
        Assert.Contains("permission-request", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "permission-request.css")));
        Assert.DoesNotContain("permission-request", roots);
        Assert.Contains("question", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "question.css")));
        Assert.DoesNotContain("question", roots);
        Assert.Contains("stop-confirm", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "stop-confirm.css")));
        Assert.DoesNotContain("stop-confirm", roots);
        Assert.Contains("image-thumb", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "image-thumb.css")));
        Assert.DoesNotContain("image-thumb", roots);
        Assert.Contains("file-explorer", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "file-explorer.css")));
        Assert.DoesNotContain("file-explorer", roots);
        Assert.Contains("process-fold", ComponentRoots(WindowResources.Read(WindowResources.SharedComponentFolder + "process-fold.css")));
        Assert.DoesNotContain("process-fold", roots);

        Assert.DoesNotContain("yes", roots);
        Assert.DoesNotContain("compact", roots);
        Assert.DoesNotContain("bubble", roots);
        Assert.DoesNotContain("flush", roots);
    }

    [Fact]
    public void EachComponentIsDrawnInOneSheetAndAComponentsSheetDrawsOnlyItsOwn()
    {
        var sheets = WindowResources.ComponentSheets().Select((name) => (name, WindowResources.Read(name))).ToList();

        var problems = DrawnTwiceProblems(sheets).ToList();

        Assert.True(sheets.Count >= 6, $"only {sheets.Count} component sheets were read");
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    /// <summary>The gate fails on a component drawn in two sheets, and on a component's sheet that draws another's class.</summary>
    [Theory]
    [InlineData("web/components.css", ".toggle { padding: 0; }", "web/components/toggle.css", ".toggle { width: 2rem; }")]
    [InlineData("web/components/select.css", ".select { padding: 0; }", "web/components/toggle.css", ".select-aside { color: var(--ink); }")]
    [InlineData("web/components.css", ".row { padding: 0; }", "web/components/toggle.css", ".toggle, .row.toggle-row { width: 2rem; }")]
    [InlineData("web/components.css", ".row { padding: 0; }", "web/components/toggle.css", ".toggled { width: 2rem; }")]
    public void AComponentDrawnTwiceOrElsewhereFails(string first, string firstCss, string second, string secondCss)
    {
        Assert.NotEmpty(DrawnTwiceProblems([(first, firstCss), (second, secondCss)]));
    }

    [Theory]
    [InlineData("web/components.css", ".row { padding: 0; }\n.row.compact { padding: 0; }", "web/components/toggle.css", ".toggle { width: 2rem; }\n.toggle-knob { top: 0; }")]
    [InlineData("web/components.css", ".button kbd { color: inherit; }", "web/components/option-list.css", ".option-list-item[aria-current=\"true\"] .option-list-text { font-weight: 600; }")]
    [InlineData("web/components.css", "button, input { font: inherit; }", "web/components/toggle.css", "@media (prefers-reduced-motion: reduce) {\n  .toggle-knob { transition: none; }\n}")]
    public void AComponentDrawnOnceInItsOwnSheetPasses(string first, string firstCss, string second, string secondCss)
    {
        Assert.Empty(DrawnTwiceProblems([(first, firstCss), (second, secondCss)]));
    }

    /// <summary>The gate fails on each way a feature sheet restyles a component.</summary>
    [Theory]
    [InlineData(".x .button { color: var(--ink); }")]
    [InlineData(".x .button { padding: var(--sp-1); }")]
    [InlineData(".row.queue-item { border-radius: var(--radius-1); }")]
    [InlineData(".x .row:hover { background: var(--select); }")]
    [InlineData(".x .row[aria-current=\"true\"]::before { content: \"\"; }")]
    [InlineData(".x .button kbd { font-size: var(--text-1); }")]
    [InlineData(".x .row { --gap: 1rem; }")]
    [InlineData(".a, .x > .button { box-shadow: var(--shadow); }")]
    [InlineData(".x .row { margin: 0; font: inherit; }")]
    [InlineData("@media (max-width: 760px) {\n  .x .row { padding: 0; }\n}")]
    [InlineData(".x textarea { font: inherit; }")]
    [InlineData(".x input:focus { border-color: var(--focus); }")]
    [InlineData(".prose a { color: var(--accent); }")]
    [InlineData(".x summary { cursor: pointer; }")]
    [InlineData("button, input { color: inherit; }")]
    [InlineData("#pop { padding: var(--sp-1); }")]
    [InlineData(".x #pop:hover { background: var(--select); }")]
    public void ARestyleOfAComponentFails(string css)
    {
        Assert.NotEmpty(RestyleProblems("web/x.css", css, Sample, ["pop"]));
    }

    [Theory]
    [InlineData(".x .button { margin-left: auto; flex: 1 1 0; min-width: 0; justify-content: space-between; }")]
    [InlineData(".inbox .row { display: grid; grid-template-columns: 1fr 10rem; gap: var(--sp-2); width: calc(100% + 1rem); }")]
    [InlineData(".row .queue-title { color: var(--ink); }")]
    [InlineData(".row[aria-current=\"true\"] .queue-title { font-weight: 600; }")]
    [InlineData(".x:has(> .button) { padding: var(--sp-2); }")]
    [InlineData(".x:not(.row) { color: var(--ink); }")]
    [InlineData(".rows { padding: 0; }\n/* .row { color: red } */")]
    [InlineData(".x textarea:focus { min-height: 6rem; }")]
    [InlineData(".x summary { display: list-item; }")]
    [InlineData("#pop { position: fixed; z-index: 20; }")]
    [InlineData("#other { padding: 0; }\n.reading { color: var(--ink); }")]
    public void APlacementOfAComponentPasses(string css)
    {
        Assert.Empty(RestyleProblems("web/x.css", css, Sample, ["pop"]));
    }

    /// <summary>The gate fails on a control that names no component, in a script or in the page.</summary>
    [Theory]
    [InlineData("h('button', { class: 'drow-fold', onclick: () => open() }, 'More')")]
    [InlineData("h('li', {\n  class: `sel-option${at ? ' cursor' : ''}`,\n  role: 'option',\n}, text)")]
    [InlineData("h('a', { class: 'edge-link', href: '#' }, title)")]
    [InlineData("h('span', { class: 'mark-weight', 'data-mark': '1', tabindex: last ? '0' : null }, text)")]
    [InlineData("h('h3', { class: 'doc-title', onclick: onTitle }, title)")]
    [InlineData("h('input', { id: 'x', class: open ? 'field' : 'rename-box' })")]
    [InlineData("<input id=\"search\" class=\"side-input\" type=\"search\">")]
    [InlineData("html`<button class=\"drow-fold\" @click=${() => open()}>More</button>`")]
    [InlineData("html`<li class=\"sel-option${at ? ' cursor' : ''}\"\n  role=\"option\">${text}</li>`")]
    [InlineData("html`<a class=\"edge-link\" href=\"#\">${title}</a>`")]
    [InlineData("html`<span class=\"mark-weight\" tabindex=${last ? '0' : nothing}>${text}</span>`")]
    [InlineData("html`<h3 class=\"doc-title\" @click=${onTitle}>${title}</h3>`")]
    [InlineData("html`<input id=\"x\" class=${open ? 'field' : 'rename-box'}>`")]
    [InlineData("html`<ul>${items.map((item) => html`<li class=\"pick\" role=\"option\">${item}</li>`)}</ul>`")]
    [InlineData("html`<p>${a > b ? 'more' : 'less'}</p><button class=\"close-x\">x</button>`")]
    public void AControlWithoutAComponentFails(string source)
    {
        var problems = ControlsIn("web/x.js", source).SelectMany((control) => UndrawnProblems(control, Sample)).ToList();

        Assert.NotEmpty(problems);
    }

    [Theory]
    [InlineData("h('button', { class: 'button copy', title: 'Copy' }, 'Copy')")]
    [InlineData("h('button', { role: 'radio', 'aria-checked': 'true' }, text)")]
    [InlineData("h('div', { class: 'lens-top' }, h('span', { class: 'note' }))")]
    [InlineData("h('li', { class: 'row option sel-option', role: 'option' }, text)")]
    [InlineData("h('div', { class: 'set', role: 'group', tabindex: '-1' })")]
    [InlineData("// h('div', {class: 'x', onclick: f}, child, 'text')")]
    [InlineData("<input id=\"search\" class=\"row search\" type=\"search\">")]
    [InlineData("html`<button class=\"button copy\" title=\"Copy\">Copy</button>`")]
    [InlineData("html`<li class=\"row option ${at ? 'cursor' : ''}\" role=\"option\">${text}</li>`")]
    [InlineData("html`<div class=\"set\" role=\"group\" tabindex=\"-1\"></div>`")]
    [InlineData("html`<p class=\"note\">${a > b ? 'more' : 'less'}</p>`")]
    [InlineData("// html`<div class=\"x\" @click=${f}></div>`")]
    public void AControlThatNamesAComponentPasses(string source)
    {
        var controls = ControlsIn("web/x.js", source).ToList();

        Assert.Empty(controls.SelectMany((control) => UndrawnProblems(control, Sample)));
    }

    [Fact]
    public void AControlsIdIsRead()
    {
        var controls = ControlsIn("web/x.js", "h('button', { id: 'comment-pop', class: 'button float', hidden: true })\n<input id=\"search\" type=\"search\">\nhtml`<button id=\"pop-close\" class=\"button\"></button>`").ToList();

        Assert.Equal(["comment-pop", "pop-close", "search"], controls.SelectMany((control) => control.Ids).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("h('button', { class: 'button chat-close', title: 'Close the chat' }, 'Close')", "chat-close")]
    [InlineData("{ class: q.context ? 'row question-head' : 'question-head', 'data-stop': '' }", "question-head")]
    [InlineData("{ class: step.tone ? `button tone-${step.tone}` : 'button', onclick: () => go(state) }", "tone-")]
    [InlineData("{ class: `row ${open ? 'open' : 'shut'}` }", "open shut")]
    [InlineData("{ class: 'pane pane-at', title: 'a row button' }", "")]
    [InlineData("{ class: q.context ? 'row pane-title' : 'doc-title' }", "pane-title")]
    [InlineData("html`<button class=\"button chat-close\" @click=${close}>Close</button>`", "chat-close")]
    [InlineData("html`<li class=\"row ${open ? 'open' : 'shut'}\"></li>`", "open shut")]
    [InlineData("html`<p class=${q.context ? 'row question-head' : 'question-head'}></p>`", "question-head")]
    public void AClassBesideAComponentIsTheComponents(string js, string expected)
    {
        var found = CoClasses(js, Sample).Order(StringComparer.Ordinal);

        Assert.Equal(expected, string.Join(' ', found));
    }

    /// <summary>The classes every component sheet starts a rule with.</summary>
    private static HashSet<string> WindowRoots() =>
        WindowResources.ComponentSheets().SelectMany((name) => ComponentRoots(WindowResources.Read(name))).ToHashSet();

    /// <summary>
    /// Each class that starts a rule in two component sheets, and each class a component's own sheet in web/components
    /// starts a rule with that is not its component's name or a part of it, its name and a dash, as toggle-knob.
    /// </summary>
    private static IEnumerable<string> DrawnTwiceProblems(IEnumerable<(string Name, string Css)> sheets)
    {
        var drawnIn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, css) in sheets)
        {
            var own = WindowResources.IsComponentSheet(name) ? Path.GetFileNameWithoutExtension(name) : null;
            foreach (var root in ComponentRoots(css).Order(StringComparer.Ordinal))
            {
                if (own is not null && root != own && !root.StartsWith(own + "-", StringComparison.Ordinal))
                {
                    yield return $"{name} draws .{root}: a component's own sheet draws only .{own} and its parts, .{own}-*";
                }

                if (drawnIn.TryGetValue(root, out var first))
                {
                    yield return $".{root} is drawn in {first} and {name}: a component is drawn in one sheet";
                    continue;
                }

                drawnIn[root] = name;
            }
        }
    }

    /// <summary>Every control the window's scripts and its page make.</summary>
    private static IEnumerable<Control> WindowControls() =>
        WindowResources.Named(".js").Concat(WindowResources.Named(".html")).SelectMany((name) => ControlsIn(name, WindowResources.Read(name)));

    /// <summary>
    /// The controls a source makes: each h() call of a control's tag, with a control's role, with a tabindex the keys
    /// reach or with an onclick, and each tag of a control written as HTML. A control keeps where it is, its tag, the
    /// class lists its class attribute's literals give it and its id.
    /// </summary>
    private static IEnumerable<Control> ControlsIn(string name, string source)
    {
        var code = LineComment().Replace(source, "");
        foreach (Match call in ElementCall().Matches(code))
        {
            var attributes = ObjectAt(code, call.Index + call.Length - 1);
            var tag = call.Groups["tag"].Value;
            var role = Role().Match(attributes);
            var inRole = role.Success && ControlRoles.Contains(role.Groups["role"].Value);
            var acts = Reachable().IsMatch(attributes) || Clickable().IsMatch(attributes);
            if (!ControlTags.Contains(tag) && !inRole && !acts)
            {
                continue;
            }

            var lists = new List<string>();
            var attribute = ClassAttribute().Match(attributes);
            if (attribute.Success)
            {
                var at = attribute.Index + attribute.Length;
                ScanLiterals(attributes, ref at, lists);
            }

            var ids = Id().Matches(attributes).Select((id) => id.Groups["id"].Value).ToList();
            yield return new Control($"{name}:{WindowResources.LineOf(code, call.Index)}", tag, lists, ids);
        }

        foreach (var tag in LitTemplates.Tags(code))
        {
            var role = tag["role"];
            var inRole = role is not null && ControlRoles.Contains(role.Text);
            var tabindex = tag["tabindex"];
            var reachable = tabindex is not null && tabindex.Text != "-1";
            var clicked = tag["@click"] is not null || tag["onclick"] is not null;
            if (!ControlTags.Contains(tag.Name) && !inRole && !reachable && !clicked)
            {
                continue;
            }

            var id = tag["id"];
            List<string> ids = id is null || id.Holes.Count > 0 ? [] : [id.Text];
            yield return new Control($"{name}:{WindowResources.LineOf(code, tag.Index)}", tag.Name, ClassLists(tag["class"]), ids);
        }

        foreach (Match control in HtmlControl().Matches(LitTemplates.WithoutTemplates(source)))
        {
            var attributes = control.Groups["attributes"].Value;
            var lists = HtmlClass().Matches(attributes).Select((match) => match.Groups["value"].Value).ToList();
            var ids = HtmlId().Matches(attributes).Select((match) => match.Groups["value"].Value).ToList();
            yield return new Control($"{name}:{WindowResources.LineOf(source, control.Index)}", control.Groups["tag"].Value, lists, ids);
        }
    }

    /// <summary>The object literal from the brace at open to its closing brace, strings skipped whole.</summary>
    private static string ObjectAt(string js, int open)
    {
        var depth = 0;
        var at = open;
        while (at < js.Length)
        {
            var c = js[at];
            if (c is '\'' or '"' or '`')
            {
                var end = js.IndexOf(c, at + 1);
                at = end < 0 ? js.Length : end + 1;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return js[open..(at + 1)];
                }
            }

            at++;
        }

        return js[open..];
    }

    /// <summary>Each class list of a control that names no component: its look would be a feature's own.</summary>
    private static IEnumerable<string> UndrawnProblems(Control control, HashSet<string> roots)
    {
        foreach (var list in control.ClassLists)
        {
            var words = ClassWord().Matches(list).Select((word) => word.Value).ToList();
            if (words.Count == 0 || words.Any(roots.Contains))
            {
                continue;
            }

            yield return $"{control.Where}: a {control.Tag} with class '{list.Trim()}' names no component: a component sheet draws every control";
        }
    }

    /// <summary>The class each rule of components.css starts its selector with: the components and their parts.</summary>
    private static HashSet<string> ComponentRoots(string css)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match rule in Rule().Matches(WindowResources.WithoutComments(css)))
        {
            foreach (var selector in TopLevel(rule.Groups["selector"].Value, ','))
            {
                var first = ClassName().Match(WithoutArguments(selector));
                if (first.Success)
                {
                    roots.Add(first.Groups["name"].Value);
                }
            }
        }

        return roots;
    }

    /// <summary>
    /// The classes a script sets beside a component's in one literal of a class: attribute, or in one class list of a
    /// lit-html template's tag. Each literal is one class list, so the two sides of a ternary are two lists; a
    /// template's list holds its text and what its interpolations add.
    /// </summary>
    private static IEnumerable<string> CoClasses(string js, HashSet<string> roots)
    {
        var lists = new List<string>();
        foreach (Match attribute in ClassAttribute().Matches(js))
        {
            var at = attribute.Index + attribute.Length;
            ScanLiterals(js, ref at, lists);
        }

        lists.AddRange(LitTemplates.Tags(js).SelectMany((tag) => ClassLists(tag["class"])));
        foreach (var list in lists)
        {
            var words = ClassWord().Matches(list).Select((word) => word.Value).Distinct(StringComparer.Ordinal).ToList();
            if (!words.Any(roots.Contains))
            {
                continue;
            }

            foreach (var word in words.Where((word) => !roots.Contains(word)))
            {
                yield return word;
            }
        }
    }

    /// <summary>
    /// The class lists a template's class attribute gives its tag. A quoted value is one list, its text and the
    /// literals of each interpolation in it, as a template literal is; a bare interpolation gives each of its literals
    /// as a list.
    /// </summary>
    private static List<string> ClassLists(LitTemplates.Attribute? attribute)
    {
        if (attribute is null)
        {
            return [];
        }

        var literals = attribute.Holes.SelectMany(LiteralsOf).ToList();
        if (!attribute.Quoted)
        {
            return literals;
        }

        return [attribute.Text + " " + string.Join(' ', literals)];
    }

    /// <summary>Every string literal of an interpolation's code, past each comma or newline that ends one expression in it.</summary>
    private static List<string> LiteralsOf(string code)
    {
        var literals = new List<string>();
        var at = 0;
        while (at < code.Length)
        {
            ScanLiterals(code, ref at, literals);
            at++;
        }

        return literals;
    }

    /// <summary>
    /// Collects the string literals of one expression, from at to the comma, newline or closing bracket that ends it. A
    /// template is one literal: its text, then the literals of each interpolation in it.
    /// </summary>
    private static void ScanLiterals(string js, ref int at, List<string> literals)
    {
        var depth = 0;
        while (at < js.Length)
        {
            var c = js[at];
            if (c is '\'' or '"')
            {
                var end = js.IndexOf(c, at + 1);
                if (end < 0)
                {
                    return;
                }

                literals.Add(js[(at + 1)..end]);
                at = end + 1;
                continue;
            }

            if (c == '`')
            {
                literals.Add(ScanTemplate(js, ref at));
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    return;
                }

                depth--;
            }
            else if (depth == 0 && c is ',' or '\n')
            {
                return;
            }

            at++;
        }
    }

    /// <summary>A template, from the backtick at at past its closing one: its text, and the literals its interpolations hold.</summary>
    private static string ScanTemplate(string js, ref int at)
    {
        var text = new StringBuilder();
        at++;
        while (at < js.Length && js[at] != '`')
        {
            if (js[at] == '$' && at + 1 < js.Length && js[at + 1] == '{')
            {
                var inner = new List<string>();
                at += 2;
                ScanLiterals(js, ref at, inner);
                at++;
                text.Append(' ').Append(string.Join(' ', inner)).Append(' ');
                continue;
            }

            text.Append(js[at]);
            at++;
        }

        at++;
        return text.ToString();
    }

    private static IEnumerable<string> RestyleProblems(string name, string css, HashSet<string> drawn, HashSet<string> ids)
    {
        var text = WindowResources.WithoutComments(css);
        foreach (Match rule in Rule().Matches(text))
        {
            var selectors = TopLevel(rule.Groups["selector"].Value, ',').Where((selector) => PlacesAComponent(selector, drawn, ids)).ToList();
            if (selectors.Count == 0)
            {
                continue;
            }

            foreach (var property in Properties(rule.Groups["body"].Value).Where((property) => !Placement().IsMatch(property)))
            {
                var line = WindowResources.LineOf(text, rule.Groups["selector"].Index + rule.Groups["selector"].Value.Length - rule.Groups["selector"].Value.TrimStart().Length);
                yield return $"{name}:{line}: {string.Join(", ", selectors)} sets {property}: a feature only places a component, its component sheet draws it";
            }
        }
    }

    /// <summary>
    /// True when the selector's subject is a component: its own compound is a control's tag or id or carries a
    /// component's class, or it carries no class of its own and sits inside a component, as <c>.button kbd</c>.
    /// </summary>
    private static bool PlacesAComponent(string selector, HashSet<string> drawn, HashSet<string> ids)
    {
        var compounds = TopLevel(selector, ' ').Where((compound) => compound.Length > 0).ToList();
        if (compounds.Count == 0)
        {
            return false;
        }

        var own = WithoutArguments(compounds[^1]);
        var tag = TagName().Match(own);
        if (tag.Success && ControlTags.Contains(tag.Value))
        {
            return true;
        }

        if (IdName().Matches(own).Any((id) => ids.Contains(id.Groups["name"].Value)))
        {
            return true;
        }

        var subject = ClassesOf(compounds[^1]);
        if (subject.Count > 0)
        {
            return subject.Any(drawn.Contains);
        }

        return compounds.SkipLast(1).Any((compound) => ClassesOf(compound).Any(drawn.Contains));
    }

    private static List<string> ClassesOf(string compound) =>
        ClassName().Matches(WithoutArguments(compound)).Select((match) => match.Groups["name"].Value).ToList();

    /// <summary>The selector without what its pseudo-classes take and its attribute tests hold, so :not(.a) names no class.</summary>
    private static string WithoutArguments(string selector)
    {
        var text = AttributeTest().Replace(selector, "");
        var shorter = Argument().Replace(text, "");
        while (shorter != text)
        {
            text = shorter;
            shorter = Argument().Replace(text, "");
        }

        return text;
    }

    /// <summary>
    /// The text split at each separator outside brackets and parentheses, each part trimmed. A space as the separator
    /// splits a selector into its compounds, at any whitespace and at each combinator.
    /// </summary>
    private static List<string> TopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var part = new StringBuilder();
        var depth = 0;
        foreach (var c in text)
        {
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }

            var compound = separator == ' ' && (char.IsWhiteSpace(c) || c is '>' or '+' or '~');
            if (depth == 0 && (c == separator || compound))
            {
                parts.Add(part.ToString().Trim());
                part.Clear();
                continue;
            }

            part.Append(c);
        }

        parts.Add(part.ToString().Trim());
        return parts;
    }

    private static IEnumerable<string> Properties(string body) =>
        body.Split(';')
            .Select((declaration) => declaration.Split(':', 2))
            .Where((pair) => pair.Length == 2)
            .Select((pair) => pair[0].Trim());

    /// <summary>A rule: its selector up to the brace, and its declarations; an at-rule's own brace never matches.</summary>
    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();

    [GeneratedRegex(@"\.(?<name>-?[_a-zA-Z][\w-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassName();

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeTest();

    [GeneratedRegex(@"\([^()]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Argument();

    /// <summary>What a feature sheet may set on a component: where it sits and how big, and how it lays out its content.</summary>
    [GeneratedRegex(@"^(margin(-[a-z]+)*|flex(-[a-z]+)*|grid(-[a-z]+)*|order|position|inset(-[a-z]+)*|top|right|bottom|left|z-index|(min-|max-)?(width|height)|display|(align|justify|place)-[a-z]+|gap|row-gap|column-gap)$", RegexOptions.CultureInvariant)]
    private static partial Regex Placement();

    [GeneratedRegex(@"\bclass:\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ClassAttribute();

    /// <summary>An h() call with a literal tag and an attribute object, up to the object's opening brace.</summary>
    [GeneratedRegex(@"\bh\(\s*'(?<tag>[a-z][a-z0-9]*)',\s*\{", RegexOptions.CultureInvariant)]
    private static partial Regex ElementCall();

    [GeneratedRegex(@"\brole:\s*'(?<role>[a-z]+)'", RegexOptions.CultureInvariant)]
    private static partial Regex Role();

    /// <summary>A tabindex that puts the element in the keys' order: any value but -1.</summary>
    [GeneratedRegex(@"\btabindex:(?>\s*)(?!'-1')", RegexOptions.CultureInvariant)]
    private static partial Regex Reachable();

    [GeneratedRegex(@"\bonclick:", RegexOptions.CultureInvariant)]
    private static partial Regex Clickable();

    /// <summary>A comment line, its text dropped and its newline kept, so an example in it makes no control.</summary>
    [GeneratedRegex(@"^[ \t]*//[^\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"(?<![\w-])id:\s*'(?<id>[\w-]+)'", RegexOptions.CultureInvariant)]
    private static partial Regex Id();

    [GeneratedRegex(@"<(?<tag>a|button|input|select|summary|textarea)\b(?<attributes>[^>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlControl();

    [GeneratedRegex(@"\bclass=[""](?<value>[^""]*)[""]", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlClass();

    [GeneratedRegex(@"\bid=[""](?<value>[^""]*)[""]", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlId();

    /// <summary>The element a compound names, when it starts with one.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9]*", RegexOptions.CultureInvariant)]
    private static partial Regex TagName();

    [GeneratedRegex(@"#(?<name>-?[_a-zA-Z][\w-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex IdName();

    /// <summary>A control the window makes: where it is, its tag, the class lists it may carry and its ids.</summary>
    private sealed record Control(string Where, string Tag, List<string> ClassLists, List<string> Ids);

    [GeneratedRegex(@"(?<![\w-])[a-zA-Z][\w-]*", RegexOptions.CultureInvariant)]
    private static partial Regex ClassWord();
}
