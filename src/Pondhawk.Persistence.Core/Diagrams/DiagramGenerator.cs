using System.Text;
using System.Web;
using Pondhawk.Persistence.Core.Ddl;
using Pondhawk.Persistence.Core.Introspection;
using Pondhawk.Persistence.Core.Models;

namespace Pondhawk.Persistence.Core.Diagrams;

public static class DiagramGenerator
{
    private static readonly string[] GroupColors =
    [
        "#0f3460", "#5b2c6f", "#1a5276", "#6c3461", "#0e6655",
        "#7d6608", "#784212", "#1b4f72", "#633974", "#0b5345",
        "#6e2c00", "#1f618d"
    ];

    public static string Generate(List<Model> models, List<SchemaFileEnum>? enums = null,
        string? projectName = null, string? description = null)
    {
        var sorted = DdlGeneratorBase.TopologicalSort(models);
        var groups = ComputeGroups(sorted, enums);

        // Build ordered group list with colors and member IDs
        var groupMembers = new Dictionary<string, List<string>>();
        foreach (var kvp in groups)
            if (!groupMembers.ContainsKey(kvp.Value))
                groupMembers[kvp.Value] = [];

        // Stable ordering: keep insertion order from groups dict
        foreach (var kvp in groups)
            groupMembers[kvp.Value].Add(kvp.Key);

        var groupNames = groupMembers.Keys.ToList();
        var groupColorMap = new Dictionary<string, string>();
        for (int i = 0; i < groupNames.Count; i++)
            groupColorMap[groupNames[i]] = GroupColors[i % GroupColors.Length];

        // Build parent→children tree from FK relationships (for FK-chain groups)
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in sorted)
        {
            if (model.IsView) continue;
            foreach (var fk in model.ForeignKeys)
            {
                var parent = fk.PrincipalTable;
                // Only include if both are in the same FK-chain group
                var childId = $"table-{model.Name}";
                var parentId = $"table-{parent}";
                if (groups.TryGetValue(childId, out var cg) && groups.TryGetValue(parentId, out var pg)
                    && cg == pg && cg != "Unconnected" && cg != "Views")
                {
                    if (!childrenOf.ContainsKey(parent))
                        childrenOf[parent] = [];
                    if (!childrenOf[parent].Contains(model.Name))
                        childrenOf[parent].Add(model.Name);
                }
            }
        }

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        var title = string.IsNullOrWhiteSpace(projectName) ? "ER Diagram" : $"ER Diagram — {Esc(projectName)}";
        sb.AppendLine($"<title>{title}</title>");
        sb.AppendLine("<style>");
        sb.Append(GetCss());
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Title bar
        var titleName = string.IsNullOrWhiteSpace(projectName) ? "ER Diagram" : Esc(projectName);
        sb.AppendLine("<div id=\"titlebar\">");
        sb.AppendLine($"  <span class=\"titlebar-name\">{titleName}</span>");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"  <span class=\"titlebar-desc\">{Esc(description)}</span>");
        sb.AppendLine("  <span class=\"titlebar-spacer\"></span>");
        var now = DateTime.Now;
        var timestamp = now.ToString("ddd, MMM d, yyyy h:mm tt");
        sb.AppendLine($"  <span class=\"titlebar-date\">Generated {Esc(timestamp)}</span>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div id=\"canvas\">");
        sb.AppendLine("<svg id=\"lines\"></svg>");

        // Render table boxes
        for (int i = 0; i < sorted.Count; i++)
        {
            var model = sorted[i];
            var group = groups.GetValueOrDefault($"table-{model.Name}", "Unconnected");
            sb.Append(RenderTable(model, i, group));
        }

        // Render enum boxes
        if (enums is not null)
        {
            for (int i = 0; i < enums.Count; i++)
            {
                sb.Append(RenderEnum(enums[i], sorted.Count + i));
            }
        }

        sb.AppendLine("</div>");

        // Sidebar
        sb.AppendLine("<div id=\"sidebar\">");
        sb.AppendLine("  <div class=\"sidebar-header\">Entities</div>");
        sb.AppendLine("  <div class=\"search-wrap\"><input id=\"search\" type=\"text\" placeholder=\"Search tables...\" autocomplete=\"off\" /></div>");
        sb.AppendLine("  <div id=\"search-results\"></div>");
        sb.AppendLine($"  <div class=\"sidebar-row group-btn active\" data-group=\"__all__\">");
        sb.AppendLine($"    <span class=\"group-dot\" style=\"background:#4a9eff\"></span>");
        sb.AppendLine($"    <span class=\"group-name\">All</span>");
        sb.AppendLine($"    <span class=\"group-count\">{groups.Count}</span>");
        sb.AppendLine($"  </div>");
        sb.AppendLine("  <div class=\"sidebar-divider\"></div>");
        var flatGroups = new HashSet<string> { "Unconnected", "Views", "Enums" };
        foreach (var gn in groupNames)
        {
            var color = groupColorMap[gn];
            var count = groupMembers[gn].Count;
            sb.AppendLine($"  <div class=\"sidebar-row group-btn\" data-group=\"{Esc(gn)}\">");
            sb.AppendLine($"    <span class=\"group-dot\" style=\"background:{color}\"></span>");
            sb.AppendLine($"    <span class=\"group-name\">{Esc(gn)}</span>");
            sb.AppendLine($"    <span class=\"group-count\">{count}</span>");
            sb.AppendLine($"  </div>");

            // Render FK dependency tree for chain groups
            if (!flatGroups.Contains(gn) && count > 1)
            {
                sb.AppendLine($"  <div class=\"tree-container\" data-group=\"{Esc(gn)}\">");
                sb.Append(RenderTree(gn, childrenOf, color, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                sb.AppendLine("  </div>");
            }
        }
        sb.AppendLine("</div>");

        // Zoom toolbar
        sb.AppendLine("<div id=\"zoom-toolbar\">");
        sb.AppendLine("  <button id=\"zoom-out\" title=\"Zoom out\">&#8722;</button>");
        sb.AppendLine("  <span id=\"zoom-level\">100%</span>");
        sb.AppendLine("  <button id=\"zoom-in\" title=\"Zoom in\">&#43;</button>");
        sb.AppendLine("  <button id=\"zoom-fit\" title=\"Zoom to fit\">Fit</button>");
        sb.AppendLine("</div>");

        sb.AppendLine("<script>");
        sb.Append(GetDagreJs());
        sb.AppendLine("</script>");
        sb.AppendLine("<script>");
        sb.Append(GetJavaScript(sorted, enums, groupNames, groupColorMap, groupMembers));
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    internal static Dictionary<string, string> ComputeGroups(List<Model> sorted, List<SchemaFileEnum>? enums)
    {
        var groups = new Dictionary<string, string>();

        // Build lookup: table name → Model (non-views only)
        var tableByName = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in sorted)
            if (!m.IsView)
                tableByName[m.Name] = m;

        // Build sets of tables that participate in FK relationships
        var hasOutgoingFk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isReferencedByFk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in sorted)
        {
            if (m.IsView) continue;
            foreach (var fk in m.ForeignKeys)
            {
                if (tableByName.ContainsKey(fk.PrincipalTable))
                {
                    hasOutgoingFk.Add(m.Name);
                    isReferencedByFk.Add(fk.PrincipalTable);
                }
            }
        }

        // For each non-view table, walk FK chain to find root
        string FindRoot(string name, HashSet<string> visited)
        {
            if (!tableByName.TryGetValue(name, out var model))
                return name;
            foreach (var fk in model.ForeignKeys)
            {
                if (tableByName.ContainsKey(fk.PrincipalTable) && !visited.Contains(fk.PrincipalTable))
                {
                    visited.Add(fk.PrincipalTable);
                    return FindRoot(fk.PrincipalTable, visited);
                }
            }
            return name;
        }

        foreach (var m in sorted)
        {
            var id = $"table-{m.Name}";
            if (m.IsView)
            {
                groups[id] = "Views";
            }
            else if (!hasOutgoingFk.Contains(m.Name) && !isReferencedByFk.Contains(m.Name))
            {
                groups[id] = "Unconnected";
            }
            else
            {
                var root = FindRoot(m.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { m.Name });
                groups[id] = root;
            }
        }

        if (enums is not null)
        {
            foreach (var e in enums)
                groups[$"enum-{e.Name}"] = "Enums";
        }

        return groups;
    }

    private static string RenderTable(Model model, int index, string group)
    {
        var sb = new StringBuilder();
        var tableId = $"table-{Esc(model.Name)}";
        var boxClass = model.IsView ? "view-box" : "table-box";
        var headerClass = model.IsView ? "view-header" : "table-header";
        var label = model.IsView ? $"{Esc(model.Name)} (view)" : Esc(model.Name);
        sb.AppendLine($"<div class=\"{boxClass}\" id=\"{tableId}\" data-index=\"{index}\" data-group=\"{Esc(group)}\">");
        sb.AppendLine($"  <div class=\"{headerClass}\">{label}</div>");

        foreach (var attr in model.Attributes)
        {
            var cssClass = "table-row";
            var icons = new StringBuilder();

            if (attr.IsPrimaryKey)
            {
                cssClass += " pk-row";
                icons.Append("<span class=\"icon pk-icon\" title=\"Primary Key\">&#128273;</span>");
            }

            var isFk = model.ForeignKeys.Any(fk => fk.Columns.Contains(attr.Name));
            if (isFk)
            {
                cssClass += " fk-row";
                icons.Append("<span class=\"icon fk-icon\" title=\"Foreign Key\">&#8594;</span>");
            }

            var isUnique = model.Indexes.Any(idx => idx.IsUnique && idx.Columns.Contains(attr.Name));
            if (isUnique)
            {
                cssClass += " unique-row";
                icons.Append("<span class=\"icon unique-icon\" title=\"Unique\">U</span>");
            }

            if (attr.IsNullable)
                cssClass += " nullable-row";

            var typeStr = Esc(attr.DataType);
            var nameStr = Esc(attr.Name);
            var nullMark = attr.IsNullable ? "" : "<span class=\"icon nn-icon\" title=\"NOT NULL\">!</span>";

            sb.AppendLine($"  <div class=\"{cssClass}\" data-col=\"{Esc(attr.Name)}\">");
            sb.AppendLine($"    <span class=\"col-icons\">{icons}</span>");
            sb.AppendLine($"    <span class=\"col-name\">{nameStr}</span>");
            sb.AppendLine($"    <span class=\"col-type\">{typeStr}</span>");
            sb.AppendLine($"    <span class=\"col-constraints\">{nullMark}</span>");
            sb.AppendLine("  </div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string RenderEnum(SchemaFileEnum enumDef, int index)
    {
        var sb = new StringBuilder();
        var enumId = $"enum-{Esc(enumDef.Name)}";
        sb.AppendLine($"<div class=\"enum-box\" id=\"{enumId}\" data-index=\"{index}\" data-group=\"Enums\">");
        sb.AppendLine($"  <div class=\"enum-header\">{Esc(enumDef.Name)}</div>");

        foreach (var val in enumDef.Values)
        {
            sb.AppendLine($"  <div class=\"enum-row\">{Esc(val.Name)}</div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string RenderTree(string name, Dictionary<string, List<string>> childrenOf,
        string color, int depth, HashSet<string> visited)
    {
        if (visited.Contains(name)) return "";
        visited.Add(name);

        var sb = new StringBuilder();
        var hasChildren = childrenOf.TryGetValue(name, out var children) && children.Count > 0;
        var indent = depth * 16;

        sb.AppendLine($"<div class=\"tree-node\" data-entity=\"table-{Esc(name)}\" style=\"padding-left:{indent + 14}px\">");
        if (hasChildren)
            sb.Append($"<span class=\"tree-toggle\">&#9662;</span>");
        else
            sb.Append($"<span class=\"tree-leaf\"></span>");
        sb.Append($"<span class=\"group-dot\" style=\"background:{color}\"></span>");
        sb.Append($"<span class=\"tree-label\">{Esc(name)}</span>");
        sb.AppendLine("</div>");

        if (hasChildren)
        {
            sb.AppendLine($"<div class=\"tree-children\">");
            foreach (var child in children!)
                sb.Append(RenderTree(child, childrenOf, color, depth + 1, visited));
            sb.AppendLine("</div>");
        }

        return sb.ToString();
    }

    private static string Esc(string s) => HttpUtility.HtmlEncode(s);

    private static string GetCss() => """
        :root { --sidebar-w: 220px; }
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { background: #1a1a2e; overflow: hidden; font-family: 'Segoe UI', system-ui, sans-serif; color: #e0e0e0; }
        #canvas { position: relative; width: 100vw; height: 100vh; transform-origin: 0 0; margin-left: var(--sidebar-w); padding-top: 40px; }
        #lines { position: absolute; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; z-index: 0; }
        #lines line { stroke: #4a9eff; stroke-width: 1.5; opacity: 0.5; }
        #lines line.highlight { stroke: #ffd700; stroke-width: 2.5; opacity: 1; }
        .table-box { position: absolute; background: #16213e; border: 1px solid #0f3460; border-radius: 6px; min-width: 200px; box-shadow: 0 2px 8px rgba(0,0,0,0.3); z-index: 1; }
        .table-header { background: #0f3460; color: #e0e0e0; font-weight: 600; padding: 8px 12px; border-radius: 5px 5px 0 0; cursor: move; user-select: none; font-size: 14px; }
        .table-row { display: flex; align-items: center; padding: 4px 12px; border-top: 1px solid #0f3460; font-size: 13px; gap: 6px; }
        .table-row:hover { background: #1a2a4a; }
        .pk-row { background: rgba(255, 215, 0, 0.08); }
        .fk-row .col-name { color: #4a9eff; }
        .nullable-row .col-name { font-style: italic; opacity: 0.8; }
        .col-icons { min-width: 32px; font-size: 12px; }
        .col-name { flex: 1; }
        .col-type { color: #888; font-size: 12px; }
        .col-constraints { min-width: 16px; text-align: center; color: #ff6b6b; font-weight: bold; font-size: 12px; }
        .icon { margin-right: 2px; }
        .pk-icon { color: #ffd700; }
        .fk-icon { color: #4a9eff; }
        .nn-icon { color: #ff6b6b; }
        .unique-icon { color: #a78bfa; font-weight: bold; font-size: 11px; }
        .view-box { position: absolute; background: #1e2a2e; border: 1px solid #2d5a6a; border-radius: 6px; min-width: 200px; box-shadow: 0 2px 8px rgba(0,0,0,0.3); z-index: 1; border-style: dashed; }
        .view-header { background: #2d5a6a; color: #a0d0e0; font-weight: 600; padding: 8px 12px; border-radius: 5px 5px 0 0; cursor: move; user-select: none; font-size: 14px; }
        .view-box .table-row { border-top: 1px solid #2d5a6a; }
        .enum-box { position: absolute; background: #1e2a1e; border: 1px solid #2d5a2d; border-radius: 6px; min-width: 160px; box-shadow: 0 2px 8px rgba(0,0,0,0.3); z-index: 1; }
        .enum-header { background: #2d5a2d; color: #a0d0a0; font-weight: 600; padding: 8px 12px; border-radius: 5px 5px 0 0; cursor: move; user-select: none; font-size: 14px; }
        .enum-row { padding: 4px 12px; border-top: 1px solid #2d5a2d; font-size: 13px; color: #c0e0c0; }
        #titlebar { position: fixed; top: 0; left: var(--sidebar-w); right: 0; height: 40px; background: #12122a; border-bottom: 1px solid #2a2a4a; display: flex; align-items: center; padding: 0 16px; gap: 12px; z-index: 999; }
        .titlebar-name { font-weight: 700; font-size: 15px; color: #e0e0e0; }
        .titlebar-desc { font-size: 13px; color: #888; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 40%; }
        .titlebar-date { font-size: 12px; color: #666; white-space: nowrap; }
        .titlebar-spacer { flex: 1; }
        #sidebar { position: fixed; top: 0; left: 0; width: var(--sidebar-w); height: 100vh; background: #12122a; border-right: 1px solid #2a2a4a; overflow-y: auto; z-index: 1000; padding: 12px 0; }
        .sidebar-header { font-weight: 700; font-size: 15px; padding: 4px 14px 8px; color: #c0c0e0; }
        .sidebar-controls { padding: 0 14px 8px; font-size: 13px; }
        .sidebar-controls a { color: #4a9eff; text-decoration: none; cursor: pointer; }
        .sidebar-controls a:hover { text-decoration: underline; }
        .sidebar-row { display: flex; align-items: center; gap: 6px; padding: 5px 14px; cursor: pointer; font-size: 13px; }
        .sidebar-row:hover { background: #1a1a3e; }
        .sidebar-row.active { background: #1a2a4a; }
        .group-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
        .group-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .group-count { color: #666; font-size: 12px; }
        .sidebar-divider { border-top: 1px solid #2a2a4a; margin: 6px 14px; }
        .tree-container { padding: 2px 0 4px; }
        .tree-node { display: flex; align-items: center; gap: 4px; padding: 2px 14px 2px 0; cursor: pointer; font-size: 12px; color: #b0b0c0; }
        .tree-node:hover { background: #1a1a3e; }
        .tree-toggle { font-size: 10px; width: 12px; text-align: center; cursor: pointer; color: #666; flex-shrink: 0; }
        .tree-leaf { width: 12px; flex-shrink: 0; }
        .tree-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .tree-children { }
        .tree-children.collapsed { display: none; }
        .tree-node .group-dot { width: 8px; height: 8px; }
        .search-wrap { padding: 0 10px 8px; }
        #search { width: 100%; padding: 6px 10px; background: #1a1a3e; border: 1px solid #2a2a4a; border-radius: 4px; color: #e0e0e0; font-size: 13px; outline: none; }
        #search:focus { border-color: #4a9eff; }
        #search::placeholder { color: #555; }
        #search-results { display: none; }
        #search-results .search-item { display: flex; align-items: center; gap: 6px; padding: 5px 14px; cursor: pointer; font-size: 13px; }
        #search-results .search-item:hover { background: #1a1a3e; }
        .search-highlight { outline: 2px solid #ffd700; outline-offset: 2px; z-index: 2; }
        .clickable-box { cursor: pointer; }
        .clickable-box:hover { outline: 1px solid #4a9eff; outline-offset: 2px; }
        #zoom-toolbar { position: fixed; bottom: 16px; right: 16px; display: flex; align-items: center; gap: 2px; background: #12122a; border: 1px solid #2a2a4a; border-radius: 6px; padding: 4px; z-index: 999; }
        #zoom-toolbar button { background: #1a1a3e; border: 1px solid #2a2a4a; color: #e0e0e0; font-size: 14px; width: 32px; height: 28px; border-radius: 4px; cursor: pointer; display: flex; align-items: center; justify-content: center; }
        #zoom-toolbar button:hover { background: #2a2a5a; }
        #zoom-toolbar button:active { background: #3a3a6a; }
        #zoom-toolbar #zoom-fit { width: auto; padding: 0 10px; font-size: 12px; font-weight: 600; }
        #zoom-level { font-size: 12px; color: #888; min-width: 40px; text-align: center; user-select: none; }
        """;

    private static string GetJavaScript(List<Model> models, List<SchemaFileEnum>? enums,
        List<string> groupNames, Dictionary<string, string> groupColorMap,
        Dictionary<string, List<string>> groupMembers)
    {
        var sb = new StringBuilder();

        // Build FK relationship data
        sb.AppendLine("const relationships = [");
        foreach (var model in models)
        {
            foreach (var fk in model.ForeignKeys)
            {
                foreach (var col in fk.Columns)
                {
                    var prinCol = fk.PrincipalColumns.Count > 0 ? fk.PrincipalColumns[0] : "";
                    sb.AppendLine($"  {{ from: '{EscJs(model.Name)}', fromCol: '{EscJs(col)}', to: '{EscJs(fk.PrincipalTable)}', toCol: '{EscJs(prinCol)}' }},");
                }
            }
        }
        sb.AppendLine("];");
        sb.AppendLine();

        // Emit groups data
        sb.AppendLine("const groups = {");
        foreach (var gn in groupNames)
        {
            var color = groupColorMap[gn];
            var ids = groupMembers[gn];
            var idList = string.Join(", ", ids.Select(id => $"'{EscJs(id)}'"));
            sb.AppendLine($"  '{EscJs(gn)}': {{ color: '{color}', ids: [{idList}] }},");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        // Build element-to-group lookup
        sb.AppendLine("const elGroupMap = {};");
        sb.AppendLine("Object.keys(groups).forEach(gn => groups[gn].ids.forEach(id => elGroupMap[id] = gn));");
        sb.AppendLine();

        sb.Append("""
        const lrGroups = { 'Unconnected': true, 'Views': true, 'Enums': true, '__all__': true };
        let scale = 1, panX = 0, panY = 0;
        let activeGroup = '__all__';
        let allViewState = null;
        const canvas = document.getElementById('canvas');
        const svg = document.getElementById('lines');
        svg.style.overflow = 'visible';
        let sidebarWidth = 220;

        function drawLines() {
          svg.innerHTML = '';
          relationships.forEach((rel, idx) => {
            const fromEl = document.getElementById('table-' + rel.from);
            const toEl = document.getElementById('table-' + rel.to);
            if (!fromEl || !toEl) return;
            if (fromEl.offsetParent === null || toEl.offsetParent === null) return;

            const fromCx = fromEl.offsetLeft + fromEl.offsetWidth / 2;
            const fromCy = fromEl.offsetTop + fromEl.offsetHeight / 2;
            const toCx = toEl.offsetLeft + toEl.offsetWidth / 2;
            const toCy = toEl.offsetTop + toEl.offsetHeight / 2;

            let x1, y1, x2, y2;
            if (Math.abs(fromCy - toCy) > Math.abs(fromCx - toCx)) {
              if (fromCy < toCy) {
                x1 = fromCx; y1 = fromEl.offsetTop + fromEl.offsetHeight;
                x2 = toCx;   y2 = toEl.offsetTop;
              } else {
                x1 = fromCx; y1 = fromEl.offsetTop;
                x2 = toCx;   y2 = toEl.offsetTop + toEl.offsetHeight;
              }
            } else {
              if (fromCx < toCx) {
                x1 = fromEl.offsetLeft + fromEl.offsetWidth; y1 = fromCy;
                x2 = toEl.offsetLeft;                        y2 = toCy;
              } else {
                x1 = fromEl.offsetLeft;                      y1 = fromCy;
                x2 = toEl.offsetLeft + toEl.offsetWidth;     y2 = toCy;
              }
            }

            const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            line.setAttribute('x1', x1);
            line.setAttribute('y1', y1);
            line.setAttribute('x2', x2);
            line.setAttribute('y2', y2);
            line.dataset.from = rel.from;
            line.dataset.to = rel.to;
            line.dataset.idx = idx;
            var fromGroup = elGroupMap['table-' + rel.from];
            if (fromGroup && groups[fromGroup]) {
              line.style.stroke = groups[fromGroup].color;
            }
            svg.appendChild(line);
          });
        }

        function runLayout(rankdir) {
          const visibleBoxes = Array.from(document.querySelectorAll('.table-box, .view-box, .enum-box'))
            .filter(el => el.offsetParent !== null);
          if (visibleBoxes.length === 0) return;

          var g = new dagre.graphlib.Graph();
          g.setGraph({ rankdir: rankdir, ranksep: rankdir === 'TB' ? 80 : 60, nodesep: 30, marginx: 40, marginy: 40 });
          g.setDefaultEdgeLabel(function() { return {}; });

          visibleBoxes.forEach(function(el) {
            g.setNode(el.id, { width: el.offsetWidth + 20, height: el.offsetHeight + 20 });
          });

          relationships.forEach(function(rel) {
            var fromId = 'table-' + rel.from;
            var toId = 'table-' + rel.to;
            if (g.hasNode(fromId) && g.hasNode(toId)) {
              g.setEdge(toId, fromId);
            }
          });

          dagre.layout(g);

          g.nodes().forEach(function(v) {
            var node = g.node(v);
            var el = document.getElementById(v);
            if (el) {
              el.style.left = (node.x - node.width / 2) + 'px';
              el.style.top = (node.y - node.height / 2) + 'px';
            }
          });

          colorHeaders();
        }

        function colorHeaders() {
          document.querySelectorAll('.table-box, .view-box, .enum-box').forEach(function(el) {
            var gn = elGroupMap[el.id];
            if (gn && groups[gn]) {
              var header = el.querySelector('.table-header, .view-header, .enum-header');
              if (header) header.style.background = groups[gn].color;
            }
          });
        }

        function zoomToFit() {
          const visibleBoxes = Array.from(document.querySelectorAll('.table-box, .view-box, .enum-box'))
            .filter(el => el.offsetParent !== null);
          if (visibleBoxes.length === 0) return;

          var minL = Infinity, minT = Infinity, maxR = 0, maxB = 0;
          visibleBoxes.forEach(el => {
            var l = parseInt(el.style.left) || 0;
            var t = parseInt(el.style.top) || 0;
            if (l < minL) minL = l;
            if (t < minT) minT = t;
            var r = l + el.offsetWidth;
            var b = t + el.offsetHeight;
            if (r > maxR) maxR = r;
            if (b > maxB) maxB = b;
          });

          var contentW = maxR - minL;
          var contentH = maxB - minT;
          svg.setAttribute('width', maxR + 40);
          svg.setAttribute('height', maxB + 40);

          var pad = 40;
          var vw = window.innerWidth - sidebarWidth, vh = window.innerHeight;
          scale = Math.min(vw / (contentW + pad * 2), vh / (contentH + pad * 2), 1);
          panX = (vw - contentW * scale) / 2 - minL * scale;
          panY = (vh - contentH * scale) / 2 - minT * scale;
          applyTransform();
        }

        // Grid layout for flat groups (no FK edges) — arranges in rows
        // When groupSort is true, sorts entities by group so members stay adjacent
        function gridLayout(groupSort) {
          const visibleBoxes = Array.from(document.querySelectorAll('.table-box, .view-box, .enum-box'))
            .filter(el => el.offsetParent !== null);
          if (visibleBoxes.length === 0) return;

          if (groupSort) {
            var groupOrder = Object.keys(groups);
            visibleBoxes.sort(function(a, b) {
              var ga = elGroupMap[a.id] || '';
              var gb = elGroupMap[b.id] || '';
              var ia = groupOrder.indexOf(ga);
              var ib = groupOrder.indexOf(gb);
              if (ia === -1) ia = groupOrder.length;
              if (ib === -1) ib = groupOrder.length;
              return ia - ib;
            });
          }

          var gap = 20, marginX = 40, marginY = 40;
          var vw = window.innerWidth - sidebarWidth;
          var maxRowWidth = Math.max(vw - marginX * 2, 400);
          var x = marginX, y = marginY, rowHeight = 0;

          visibleBoxes.forEach(function(el) {
            var w = el.offsetWidth;
            var h = el.offsetHeight;
            if (x + w > maxRowWidth + marginX && x > marginX) {
              x = marginX;
              y += rowHeight + gap;
              rowHeight = 0;
            }
            el.style.left = x + 'px';
            el.style.top = y + 'px';
            x += w + gap;
            if (h > rowHeight) rowHeight = h;
          });

          colorHeaders();
        }

        function selectGroup(gn) {
          // Save All view state when leaving it
          if (activeGroup === '__all__' && gn !== '__all__') {
            allViewState = { scale: scale, panX: panX, panY: panY };
          }

          activeGroup = gn;
          // Update sidebar highlight
          document.querySelectorAll('.group-btn').forEach(btn => btn.classList.remove('active'));
          var activeBtn = document.querySelector('.group-btn[data-group="' + gn + '"]');
          if (activeBtn) activeBtn.classList.add('active');

          // Show/hide entities
          var allBoxes = document.querySelectorAll('.table-box, .view-box, .enum-box');
          allBoxes.forEach(el => {
            if (gn === '__all__') {
              el.style.display = '';
            } else {
              el.style.display = (el.dataset.group === gn) ? '' : 'none';
            }
          });

          // In All view, mark FK-chain entities as clickable
          allBoxes.forEach(el => {
            var eg = elGroupMap[el.id];
            if (gn === '__all__' && eg && !lrGroups[eg]) {
              el.classList.add('clickable-box');
            } else {
              el.classList.remove('clickable-box');
            }
          });

          // Flat groups use grid layout; FK-chain groups use dagre TB
          if (lrGroups[gn]) {
            gridLayout(gn === '__all__');
          } else {
            runLayout('TB');
          }

          // Restore saved All view state, or zoom-to-fit for other views
          if (gn === '__all__' && allViewState) {
            scale = allViewState.scale;
            panX = allViewState.panX;
            panY = allViewState.panY;
            applyTransform();
          } else {
            zoomToFit();
          }

          // Hide relationship lines in All view; show them for specific groups
          if (gn === '__all__') {
            svg.innerHTML = '';
          } else {
            requestAnimationFrame(drawLines);
          }
        }

        function autoSizeSidebar() {
          var sidebar = document.getElementById('sidebar');
          // Temporarily allow natural width measurement
          sidebar.style.width = 'max-content';
          var natural = sidebar.scrollWidth + 4;
          sidebar.style.width = '';
          sidebarWidth = Math.max(200, Math.min(400, natural));
          document.documentElement.style.setProperty('--sidebar-w', sidebarWidth + 'px');
        }

        autoSizeSidebar();

        // Initial layout — show all with group-sorted grid, no lines
        (function() {
          gridLayout(true);
          zoomToFit();
          document.querySelectorAll('.table-box, .view-box, .enum-box').forEach(el => {
            var eg = elGroupMap[el.id];
            if (eg && !lrGroups[eg]) el.classList.add('clickable-box');
          });
        })();

        // Sidebar click — mutually exclusive group selection
        document.querySelectorAll('.group-btn').forEach(btn => {
          btn.addEventListener('click', () => selectGroup(btn.dataset.group));
        });

        // Click entity in All view to navigate to its group
        document.querySelectorAll('.table-box, .view-box, .enum-box').forEach(el => {
          el.addEventListener('click', () => {
            if (activeGroup !== '__all__') return;
            var gn = elGroupMap[el.id];
            if (gn && !lrGroups[gn]) selectGroup(gn);
          });
        });

        function applyTransform() {
          canvas.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
          document.getElementById('zoom-level').textContent = Math.round(scale * 100) + '%';
        }

        document.addEventListener('wheel', (e) => {
          if (e.target.closest('#sidebar') || e.target.closest('#zoom-toolbar')) return;
          e.preventDefault();
          const delta = e.deltaY > 0 ? 0.9 : 1.1;
          scale *= delta;
          scale = Math.max(0.1, Math.min(3, scale));
          applyTransform();
        }, { passive: false });

        let isPanning = false, panStartX, panStartY;
        document.addEventListener('mousedown', (e) => {
          if (e.target.closest('#sidebar') || e.target.closest('#zoom-toolbar')) return;
          if (e.target === document.body || e.target === canvas || e.target.id === 'lines') {
            isPanning = true;
            panStartX = e.clientX - panX;
            panStartY = e.clientY - panY;
            e.preventDefault();
          }
        });
        document.addEventListener('mousemove', (e) => {
          if (isPanning) {
            panX = e.clientX - panStartX;
            panY = e.clientY - panStartY;
            applyTransform();
          }
        });
        document.addEventListener('mouseup', () => { isPanning = false; });

        // Zoom toolbar buttons
        document.getElementById('zoom-in').addEventListener('click', () => {
          scale = Math.min(3, scale * 1.2);
          applyTransform();
        });
        document.getElementById('zoom-out').addEventListener('click', () => {
          scale = Math.max(0.1, scale / 1.2);
          applyTransform();
        });
        document.getElementById('zoom-fit').addEventListener('click', () => {
          zoomToFit();
          applyTransform();
        });

        function clearHighlight() {
          document.querySelectorAll('.search-highlight').forEach(el => el.classList.remove('search-highlight'));
        }

        function scrollToEntity(el) {
          clearHighlight();
          el.classList.add('search-highlight');
          // Make sure entity is visible — switch to its group if needed
          var gn = elGroupMap[el.id];
          if (gn && activeGroup !== '__all__' && activeGroup !== gn) {
            selectGroup(gn);
          }
          // Zoom in and center the entity
          var vw = window.innerWidth - sidebarWidth;
          var vh = window.innerHeight;
          var elLeft = parseInt(el.style.left) || 0;
          var elTop = parseInt(el.style.top) || 0;
          var pad = 60;
          scale = Math.min(vw / (el.offsetWidth + pad * 2), vh / (el.offsetHeight + pad * 2), 1.5);
          scale = Math.max(0.5, scale);
          panX = vw / 2 - (elLeft + el.offsetWidth / 2) * scale;
          panY = vh / 2 - (elTop + el.offsetHeight / 2) * scale;
          applyTransform();
        }

        // Tree node interactions
        document.querySelectorAll('.tree-toggle').forEach(function(toggle) {
          toggle.addEventListener('click', function(e) {
            e.stopPropagation();
            var node = this.closest('.tree-node');
            var children = node.nextElementSibling;
            if (children && children.classList.contains('tree-children')) {
              children.classList.toggle('collapsed');
              this.innerHTML = children.classList.contains('collapsed') ? '&#9656;' : '&#9662;';
            }
          });
        });

        document.querySelectorAll('.tree-node').forEach(function(node) {
          node.addEventListener('click', function() {
            var entityId = this.dataset.entity;
            var el = document.getElementById(entityId);
            if (el) {
              var gn = elGroupMap[entityId];
              if (gn && activeGroup === '__all__') selectGroup(gn);
              scrollToEntity(el);
            }
          });
        });

        // Search
        (function() {
          var searchInput = document.getElementById('search');
          var resultsDiv = document.getElementById('search-results');
          var groupsDiv = document.querySelector('#sidebar .sidebar-row');
          var allEntities = Array.from(document.querySelectorAll('.table-box, .view-box, .enum-box'));
          var sidebarGroups = Array.from(document.querySelectorAll('.group-btn, .sidebar-divider, .tree-container'));

          searchInput.addEventListener('input', function() {
            var q = this.value.trim().toLowerCase();
            clearHighlight();
            if (!q) {
              resultsDiv.style.display = 'none';
              resultsDiv.innerHTML = '';
              sidebarGroups.forEach(el => el.style.display = '');
              return;
            }
            // Hide group list, show search results
            sidebarGroups.forEach(el => el.style.display = 'none');
            resultsDiv.style.display = 'block';
            resultsDiv.innerHTML = '';

            var matches = allEntities.filter(el => {
              var name = el.id.replace(/^(table|enum|view)-/, '');
              return name.toLowerCase().includes(q);
            });

            if (matches.length === 0) {
              resultsDiv.innerHTML = '<div style="padding:8px 14px;color:#666;font-size:13px;">No matches</div>';
              return;
            }

            matches.slice(0, 20).forEach(el => {
              var name = el.id.replace(/^(table|enum|view)-/, '');
              var gn = elGroupMap[el.id] || '';
              var color = (groups[gn] && groups[gn].color) || '#666';
              var item = document.createElement('div');
              item.className = 'search-item';
              item.innerHTML = '<span class="group-dot" style="background:' + color + '"></span><span class="group-name">' + name + '</span>';
              item.addEventListener('click', function() {
                scrollToEntity(el);
                searchInput.value = '';
                resultsDiv.style.display = 'none';
                resultsDiv.innerHTML = '';
                sidebarGroups.forEach(s => s.style.display = '');
              });
              resultsDiv.appendChild(item);
            });
          });

          searchInput.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
              this.value = '';
              this.dispatchEvent(new Event('input'));
              this.blur();
            }
          });
        })();

        // Drag tables
        document.querySelectorAll('.table-header, .view-header, .enum-header').forEach(header => {
          header.addEventListener('mousedown', (e) => {
            e.stopPropagation();
            const box = header.parentElement;
            const startX = (e.clientX - panX) / scale - (parseInt(box.style.left) || 0);
            const startY = (e.clientY - panY) / scale - (parseInt(box.style.top) || 0);

            function onMove(e2) {
              box.style.left = ((e2.clientX - panX) / scale - startX) + 'px';
              box.style.top = ((e2.clientY - panY) / scale - startY) + 'px';
              drawLines();
            }
            function onUp() {
              document.removeEventListener('mousemove', onMove);
              document.removeEventListener('mouseup', onUp);
            }
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
          });
        });

        // Hover highlight
        svg.addEventListener('mouseover', (e) => {
          if (e.target.tagName === 'line') {
            e.target.classList.add('highlight');
            const from = e.target.dataset.from;
            const to = e.target.dataset.to;
            const fromEl = document.getElementById('table-' + from);
            const toEl = document.getElementById('table-' + to);
            if (fromEl) fromEl.classList.add('table-highlight');
            if (toEl) toEl.classList.add('table-highlight');
          }
        });
        svg.addEventListener('mouseout', (e) => {
          if (e.target.tagName === 'line') {
            e.target.classList.remove('highlight');
            document.querySelectorAll('.table-highlight').forEach(el => el.classList.remove('table-highlight'));
          }
        });
        svg.style.pointerEvents = 'all';
        """);

        return sb.ToString();
    }

    private static string EscJs(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string GetDagreJs()
    {
        var assembly = typeof(DiagramGenerator).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Pondhawk.Persistence.Core.Diagrams.dagre.min.js");
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
