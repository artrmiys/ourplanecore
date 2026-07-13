using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public sealed partial class TakeoffTemplateConfig
{
    public void EnsureTemplatePresets()
    {
        if (Template == null)
            Template = BuildDefaultTemplate();
        if (Templates == null)
            Templates = new List<TakeoffTemplate>();

        if (Templates.Count == 0)
        {
            TakeoffTemplate legacy = Template.Clone();
            legacy.Name = DefaultTemplateName;
            Templates.Add(legacy);
            ActiveTemplateId = legacy.Id;
        }

        for (int i = 0; i < Templates.Count; i++)
        {
            TakeoffTemplate template = Templates[i];
            if (string.IsNullOrWhiteSpace(template.Id))
                template.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(template.Name))
                template.Name = i == 0 ? DefaultTemplateName : $"Template {i + 1}";
        }

        if (!Templates.Any(IsDefaultTemplate))
            Templates[0].Name = DefaultTemplateName;

        if (FindTemplateById(ActiveTemplateId) == null)
            ActiveTemplateId = DefaultTemplate().Id;

        SyncActiveTemplateSnapshot();
    }

    public TakeoffTemplate ActiveTemplate()
    {
        EnsureTemplatePresets();
        return FindTemplateById(ActiveTemplateId) ?? DefaultTemplate();
    }

    public TakeoffTemplate DefaultTemplate()
    {
        EnsureTemplatePresetsWithoutSync();
        return Templates.FirstOrDefault(IsDefaultTemplate) ?? Templates[0];
    }

    public bool ActiveTemplateIsDefault()
    {
        EnsureTemplatePresets();
        return IsDefaultTemplate(ActiveTemplate());
    }

    public void SelectTemplate(string templateId)
    {
        EnsureTemplatePresets();
        if (FindTemplateById(templateId) is not { } template)
            return;

        ActiveTemplateId = template.Id;
        SyncActiveTemplateSnapshot();
    }

    public TakeoffTemplate AddTemplateCopy(string name)
    {
        EnsureTemplatePresets();
        TakeoffTemplate copy = ActiveTemplate().Clone();
        ReassignTemplateIds(copy);
        copy.Name = UniqueTemplateName(name);
        Templates.Add(copy);
        ActiveTemplateId = copy.Id;
        SyncActiveTemplateSnapshot();
        return copy;
    }

    public void RenameActiveTemplate(string name)
    {
        EnsureTemplatePresets();
        TakeoffTemplate active = ActiveTemplate();
        if (IsDefaultTemplate(active))
            return;

        active.Name = UniqueTemplateName(name, active.Id);
        SyncActiveTemplateSnapshot();
    }

    public bool RemoveActiveTemplate()
    {
        EnsureTemplatePresets();
        TakeoffTemplate active = ActiveTemplate();
        if (IsDefaultTemplate(active) || Templates.Count <= 1)
            return false;

        Templates.Remove(active);
        ActiveTemplateId = DefaultTemplate().Id;
        SyncActiveTemplateSnapshot();
        return true;
    }

    public void ResetActiveTemplateToBuiltIn()
    {
        EnsureTemplatePresets();
        TakeoffTemplate active = ActiveTemplate();
        TakeoffTemplate builtIn = BuildDefaultTemplate();
        active.Roots = builtIn.Roots.Select(root => root.Clone()).ToList();
        SyncActiveTemplateSnapshot();
    }

    public void SyncActiveTemplateSnapshot()
    {
        EnsureTemplatePresetsWithoutSync();
        TakeoffTemplate active = FindTemplateById(ActiveTemplateId) ?? DefaultTemplate();
        ActiveTemplateId = active.Id;
        Template = active.Clone();
    }

    private static bool IsDefaultTemplate(TakeoffTemplate template) =>
        string.Equals(template.Name, DefaultTemplateName, StringComparison.OrdinalIgnoreCase);

    private TakeoffTemplate? FindTemplateById(string? templateId) =>
        string.IsNullOrWhiteSpace(templateId)
            ? null
            : Templates.FirstOrDefault(template =>
                string.Equals(template.Id, templateId, StringComparison.OrdinalIgnoreCase));

    private string UniqueTemplateName(string name, string? currentId = null)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Template" : name.Trim();
        string candidate = baseName;
        int suffix = 2;
        while (Templates.Any(template =>
            !string.Equals(template.Id, currentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(template.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private void EnsureTemplatePresetsWithoutSync()
    {
        if (Templates == null || Templates.Count == 0)
        {
            Templates = new List<TakeoffTemplate>();
            TakeoffTemplate legacy = (Template ?? BuildDefaultTemplate()).Clone();
            legacy.Name = DefaultTemplateName;
            Templates.Add(legacy);
        }

        for (int i = 0; i < Templates.Count; i++)
        {
            TakeoffTemplate template = Templates[i];
            if (string.IsNullOrWhiteSpace(template.Id))
                template.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(template.Name))
                template.Name = i == 0 ? DefaultTemplateName : $"Template {i + 1}";
        }

        if (!Templates.Any(IsDefaultTemplate))
            Templates[0].Name = DefaultTemplateName;
    }

    private static void ReassignTemplateIds(TakeoffTemplate template)
    {
        template.Id = Guid.NewGuid().ToString("N");
        foreach (TakeoffTemplateNode root in template.Roots)
            ReassignTemplateNodeIds(root);
    }

    private static void ReassignTemplateNodeIds(TakeoffTemplateNode node)
    {
        node.Id = Guid.NewGuid().ToString("N");
        foreach (TakeoffTemplateNode child in node.Children)
            ReassignTemplateNodeIds(child);
    }
}
