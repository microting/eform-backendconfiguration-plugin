/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.ComplianceReport;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using SdkDbContext = Microting.eForm.Infrastructure.MicrotingDbContext;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceReportService;

/// <summary>
/// Turns a set of SDK cases into a per-template COLUMN SCHEMA plus one KEYED cell
/// bag per case — the read model behind the Rapport view (#1166).
///
/// <para>
/// Lives for exactly one request. It caches the derived schema per
/// <c>CheckListId</c> (§4: <c>Advanced_TemplateFieldReadAll</c> is internally N+1,
/// which is fine once per template and ruinous once per case) and must NOT be
/// cached across requests — the user's <see cref="Language"/> is part of the key
/// and templates are editable.
/// </para>
///
/// <para>
/// <b>Why this is not built on <c>BackendConfigurationReportService.GenerateReportV2</c>.</b>
/// That method builds its headers from a FILTERED field list, emits its cells from
/// the UNFILTERED one, appends a blank cell for every unanswered field, and lets
/// <c>Audio</c>/<c>Movie</c>/<c>ShowPdf</c> — excluded from headers — fall through
/// to <c>default:</c> and always emit a cell. Because its cell list is positional,
/// each of those shifts every later column by one, silently. The same bug exists
/// twice (the legacy <c>GenerateReport</c> repeats it). Here the cells are a
/// <c>Dictionary&lt;string,string&gt;</c> keyed on <c>$"f{Field.Id}"</c>, one
/// exclusion list is consulted once at derivation time, and an unanswered field
/// simply has no entry — so the bug CLASS is not expressible. Neither of the two
/// existing methods is touched by this work.
/// </para>
/// </summary>
internal sealed class ComplianceReportEformProjector(
    Core core,
    SdkDbContext sdkDbContext,
    Language language,
    ILogger logger)
{
    /// <summary>
    /// Field types that get NEITHER a column NOR a cell.
    ///
    /// <para>
    /// The first eight match the shipped set at
    /// <c>BackendConfigurationReportService.cs:599-610</c> verbatim:
    /// <c>None, Picture, Audio, Movie, Signature, ShowPdf, FieldGroup, SaveButton</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberate deviation:</b> <c>ShowPicture</c> is excluded here and is NOT
    /// in that shipped list — where it falls through to <c>default:</c> and renders
    /// its raw <c>Value</c>. It is a display-only type that carries no answer, so a
    /// column for it is noise. Decided per #1166 §5; stated so the difference is
    /// not read later as an oversight.
    /// </para>
    ///
    /// <para>
    /// <c>Picture</c> is excluded from CELLS only — picture fields still drive the
    /// image references (§6), which is why the schema keeps their field ids
    /// separately.
    /// </para>
    ///
    /// <para>
    /// <c>FieldGroup</c> never actually appears: the SDK's own flattener
    /// (<c>MainElement.DataItemGetAll</c>) returns a group's CHILDREN and not the
    /// group itself. It is listed anyway, because relying on that is relying on an
    /// implementation detail of another repository.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ExcludedFieldTypes =
    [
        Constants.FieldTypes.None,
        Constants.FieldTypes.Picture,
        Constants.FieldTypes.Audio,
        Constants.FieldTypes.Movie,
        Constants.FieldTypes.Signature,
        Constants.FieldTypes.ShowPdf,
        Constants.FieldTypes.FieldGroup,
        Constants.FieldTypes.SaveButton,
        Constants.FieldTypes.ShowPicture
    ];

    private readonly Dictionary<int, TemplateSchema> _schemaCache = new();

    // ==================================================================
    // Column derivation (§4)
    // ==================================================================

    /// <summary>
    /// The column schema for one template, derived ONCE per <c>CheckListId</c> per
    /// request and cached.
    ///
    /// <para>
    /// Derivation goes through <c>Core.Advanced_TemplateFieldReadAll</c>
    /// (<c>Core.cs:4615</c>; implemented as <c>SqlController.TemplateFieldReadAll</c>,
    /// <c>SqlController.cs:639</c>) because it walks NESTED checklists and
    /// <c>FieldGroup</c> children. A derivation that read
    /// <c>Fields WHERE CheckListId = @templateId</c> would return an EMPTY column
    /// set for every compliance template measured, since they hang all of their
    /// fields off child checklists. It also inherits that method's
    /// <c>FirstOrDefaultAsync(lang) ?? FirstAsync(any)</c> label fallback, which
    /// matters because a majority of live fields have no German translation.
    /// </para>
    ///
    /// <para>
    /// A template whose derivation THROWS yields an empty column set rather than
    /// failing the whole report: the SDK path still contains a bare
    /// <c>FirstAsync</c> on <c>CheckListTranslations</c>
    /// (<c>SqlController.cs:668-670</c>) that throws when a child checklist has no
    /// translation in the user's language. One unreadable template must not blank
    /// the entire page; it is logged.
    /// </para>
    ///
    /// <para>
    /// The swallow is NOT silent to the caller: <see cref="TemplateSchema.SchemaUnavailable"/>
    /// is set, and travels on to
    /// <c>ComplianceReportTemplateGroupModel.SchemaUnavailable</c>, so #1167 can
    /// distinguish "derivation failed" from "nobody answered anything" — both of
    /// which otherwise render as a template group with zero columns and no cells.
    /// Logged at WARNING, not Error: a translation gap is an expected data
    /// condition, not a bug in this code.
    /// </para>
    /// </summary>
    public async Task<TemplateSchema> GetSchemaAsync(int checkListId)
    {
        if (_schemaCache.TryGetValue(checkListId, out var cached)) return cached;

        var schema = new TemplateSchema { CheckListId = checkListId };

        List<FieldDto> fields;
        try
        {
            fields = await core.Advanced_TemplateFieldReadAll(checkListId, language) ?? [];
        }
        catch (Exception e)
        {
            logger.LogWarning(e,
                "ComplianceReportEformProjector: could not derive the column schema for CheckListId {CheckListId} "
                + "in language {LanguageId}; the template is rendered with no answer columns and is flagged "
                + "SchemaUnavailable. The usual cause is the SDK's bare FirstAsync on CheckListTranslations "
                + "(SqlController.cs:668-670) for a child checklist with no translation in this language. "
                + "{Message}",
                checkListId, language.Id, e.Message);
            fields = [];
            schema.SchemaUnavailable = true;
        }

        // Every checklist whose name can be needed: the template itself, plus the
        // child checklist each field actually sits on (the group prefix below).
        var checkListIds = fields
            .Select(f => f.CheckListId)
            .Append(checkListId)
            .Distinct()
            .ToList();

        var names = await ResolveCheckListNames(checkListIds);
        schema.CheckListName = names.GetValueOrDefault(checkListId, string.Empty);

        // The label prefix only applies to templates that HAVE child checklists —
        // the same condition GenerateReportV2 uses at :683-698.
        var hasChildCheckLists = await sdkDbContext.CheckLists
            .AsNoTracking()
            .AnyAsync(x => x.ParentId == checkListId
                           && x.WorkflowState != Constants.WorkflowStates.Removed);

        foreach (var field in fields)
        {
            // A field whose type is null or blank gets NO column. It is not in
            // ExcludedFieldTypes, so without this it would take a column and then
            // register a null in FieldTypeById — and LoadAnswers skips a null type,
            // so the column could never receive a cell. Excluding it at derivation
            // time is what keeps "a column always has a renderable type" true.
            if (string.IsNullOrWhiteSpace(field.FieldType)) continue;

            if (field.FieldType == Constants.FieldTypes.Picture)
            {
                // Excluded from cells, kept for the image references (§6).
                schema.PictureFieldIds.Add(field.Id);
                continue;
            }

            if (ExcludedFieldTypes.Contains(field.FieldType)) continue;

            var label = field.Label ?? string.Empty;
            if (hasChildCheckLists)
            {
                var groupName = names.GetValueOrDefault(field.CheckListId, string.Empty);
                if (!string.IsNullOrEmpty(groupName) && groupName != schema.CheckListName)
                {
                    label = $"{groupName} - {label}";
                }
            }

            // ParentName is deliberately NOT used for the prefix: the SDK resolves
            // it from `x.FieldId == field.Id` where it means `field.ParentFieldId`
            // (SqlController.cs:660-663), so it can be the wrong text.
            schema.Columns.Add(new ComplianceReportColumnModel
            {
                Key = CellKey(field.Id),
                FieldId = field.Id,
                Label = label,
                FieldType = field.FieldType
            });

            schema.FieldTypeById[field.Id] = field.FieldType;
        }

        _schemaCache[checkListId] = schema;
        return schema;
    }

    /// <summary>The stable cell key for a field. See <see cref="ComplianceReportColumnModel.Key"/>.</summary>
    internal static string CellKey(int fieldId) => $"f{fieldId}";

    /// <summary>
    /// Checklist id → display name, batch-loaded, with the same
    /// <c>this language, else any language</c> fallback the SDK uses for field
    /// labels. Empty translations are skipped so a blank row cannot win over a
    /// usable one.
    /// </summary>
    private async Task<Dictionary<int, string>> ResolveCheckListNames(List<int> checkListIds)
    {
        if (checkListIds.Count == 0) return new Dictionary<int, string>();

        var rows = await sdkDbContext.CheckListTranslations
            .AsNoTracking()
            // Deliberately NOT filtered on WorkflowState: no existing consumer of
            // CheckListTranslations filters it, and a removed translation is still
            // the only name this checklist has.
            //
            // NOT because a null state would be dropped — that is true of RAW SQL
            // only. EF Core rewrites `!=` to preserve C# semantics and emits
            // `(workflow_state <> 'removed' OR workflow_state IS NULL)`, so
            // null-state rows are KEPT by such a predicate. Stated explicitly so
            // nobody "fixes" this on the strength of the SQL intuition.
            .Where(x => checkListIds.Contains(x.CheckListId))
            .Select(x => new { x.CheckListId, x.LanguageId, x.Text })
            .ToListAsync();

        return rows
            .Where(x => !string.IsNullOrEmpty(x.Text))
            .GroupBy(x => x.CheckListId)
            .ToDictionary(
                g => g.Key,
                g => (g.FirstOrDefault(x => x.LanguageId == language.Id) ?? g.First()).Text);
    }

    // ==================================================================
    // Answers and images (§2.2, §5, §6)
    // ==================================================================

    /// <summary>
    /// Loads every answer and image reference for one template's cases and returns
    /// them keyed by case id.
    ///
    /// <para>
    /// <b>Every bulk query leads with <c>FieldId</c>, never <c>CaseId</c></b>
    /// (#1160 finding 2): <c>FieldValues</c> has no index on <c>CaseId</c>, so
    /// <c>WHERE CaseId IN (…)</c> is a full table scan while
    /// <c>WHERE FieldId IN (…) AND CaseId IN (…)</c> uses
    /// <c>IX_field_values_field_id</c>. The field ids come from the schema, which is
    /// already in hand. Nothing here runs inside a per-case loop, and the
    /// projection is explicit — <c>FieldValue.Value</c> is <c>longtext</c> and
    /// <c>SELECT *</c> would drag it plus five geolocation columns for every row.
    /// </para>
    /// </summary>
    public async Task<TemplateProjection> ProjectAsync(int checkListId, List<int> caseIds)
    {
        var schema = await GetSchemaAsync(checkListId);
        var projection = new TemplateProjection { Schema = schema };

        if (caseIds.Count == 0) return projection;

        await LoadAnswers(schema, caseIds, projection);
        await LoadImages(schema, caseIds, projection);

        return projection;
    }

    private async Task LoadAnswers(TemplateSchema schema, List<int> caseIds, TemplateProjection projection)
    {
        var answerableFieldIds = schema.Columns.Select(c => c.FieldId).ToList();
        if (answerableFieldIds.Count == 0) return;

        var answers = await sdkDbContext.FieldValues
            .AsNoTracking()
            // FieldId leads, and is the ONLY indexed predicate available here.
            .Where(x => x.FieldId.HasValue && answerableFieldIds.Contains(x.FieldId.Value)
                        && x.CaseId.HasValue && caseIds.Contains(x.CaseId.Value)
                        && x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => new AnswerRow
            {
                Id = x.Id,
                CaseId = x.CaseId.Value,
                FieldId = x.FieldId.Value,
                Value = x.Value
            })
            .ToListAsync();

        if (answers.Count == 0) return;

        // A repeated element can leave more than one FieldValue per (case, field).
        // Take the highest Id — the most recently written — deterministically,
        // rather than letting row order decide.
        var latest = answers
            .GroupBy(a => (a.CaseId, a.FieldId))
            .Select(g => g.OrderByDescending(a => a.Id).First())
            .ToList();

        var optionLabels = await LoadOptionLabels(schema, latest);
        var entityNames = await LoadEntityNames(schema, latest);

        foreach (var answer in latest)
        {
            // No entry ⇒ the field was excluded at derivation time (an excluded
            // type, a picture field, or a field with no type at all), so it has no
            // column and therefore gets no cell. Every field that DOES have an
            // entry has a non-blank type, guaranteed by GetSchemaAsync.
            if (!schema.FieldTypeById.TryGetValue(answer.FieldId, out var fieldType)) continue;

            var rendered = Render(fieldType, answer.FieldId, answer.Value, optionLabels, entityNames);
            // NO empty-slot placeholder. An unrenderable or empty answer is simply
            // absent, which is what "unanswered" means to #1167.
            if (rendered == null) continue;

            if (!projection.CellsByCaseId.TryGetValue(answer.CaseId, out var cells))
            {
                cells = new Dictionary<string, string>();
                projection.CellsByCaseId[answer.CaseId] = cells;
            }

            cells[CellKey(answer.FieldId)] = rendered;
        }
    }

    /// <summary>
    /// One query for the option rows and one for their translations, for EVERY
    /// select field in the template — instead of the shipped code's
    /// <c>FieldOptions</c> + <c>FieldOptionTranslations</c> lookup per cell inside
    /// two nested loops. The translation fallback is the batched equivalent of
    /// <c>FirstOrDefault(lang) ?? First(any)</c>; the shipped code's bare
    /// <c>FirstAsync</c> throws for a user whose language has no option translation.
    /// </summary>
    private async Task<Dictionary<(int FieldId, string Key), string>> LoadOptionLabels(
        TemplateSchema schema, List<AnswerRow> answers)
    {
        var optionFieldIds = answers
            .Select(a => a.FieldId)
            .Distinct()
            .Where(id => schema.FieldTypeById.GetValueOrDefault(id)
                is Constants.FieldTypes.SingleSelect or Constants.FieldTypes.MultiSelect)
            .ToList();

        if (optionFieldIds.Count == 0) return new Dictionary<(int, string), string>();

        var options = await sdkDbContext.FieldOptions
            .AsNoTracking()
            // Deliberately NOT filtered on WorkflowState, matching the shipped
            // reference (BackendConfigurationReportService.cs:863-865, :886-887).
            // The answer is HISTORICAL: the worker picked this option, and the
            // option's current lifecycle state cannot retroactively unpick it — a
            // `!= removed` predicate would render the answer as no cell at all.
            //
            // That is the WHOLE reason. It is NOT that null states would drop:
            // EF Core rewrites `!=` to preserve C# semantics and emits
            // `(workflow_state <> 'removed' OR workflow_state IS NULL)`, so a
            // null-state row is KEPT — the NULL-swallows-the-row behaviour is raw
            // SQL only. Same reasoning as ResolveCheckListNames above.
            .Where(x => optionFieldIds.Contains(x.FieldId))
            .Select(x => new { x.Id, x.FieldId, x.Key })
            .ToListAsync();

        if (options.Count == 0) return new Dictionary<(int, string), string>();

        var optionIds = options.Select(x => x.Id).ToList();
        var translations = await sdkDbContext.FieldOptionTranslations
            .AsNoTracking()
            .Where(x => optionIds.Contains(x.FieldOptionId))
            .Select(x => new { x.FieldOptionId, x.LanguageId, x.Text })
            .ToListAsync();

        var textByOptionId = translations
            .Where(x => !string.IsNullOrEmpty(x.Text))
            .GroupBy(x => x.FieldOptionId)
            .ToDictionary(
                g => g.Key,
                g => (g.FirstOrDefault(x => x.LanguageId == language.Id) ?? g.First()).Text);

        var result = new Dictionary<(int FieldId, string Key), string>();
        foreach (var option in options)
        {
            if (option.Key == null) continue;
            if (!textByOptionId.TryGetValue(option.Id, out var text)) continue;
            // Two live options sharing a key on one field is a data anomaly; the
            // first wins deterministically rather than throwing.
            result.TryAdd((option.FieldId, option.Key), text);
        }

        return result;
    }

    /// <summary>
    /// One query for every entity id referenced by the group's entity answers.
    /// <c>"null"</c>, empty and unparseable values are dropped BEFORE the parse —
    /// live data holds over a thousand rows whose value is the literal string
    /// <c>"null"</c> — and a missing entity yields no cell instead of the
    /// <c>NullReferenceException</c> the shipped code raises at
    /// <c>BackendConfigurationReportService.cs:907-911</c>.
    /// </summary>
    private async Task<Dictionary<int, string>> LoadEntityNames(TemplateSchema schema, List<AnswerRow> answers)
    {
        var entityIds = answers
            .Where(a => schema.FieldTypeById.GetValueOrDefault(a.FieldId)
                is Constants.FieldTypes.EntitySearch or Constants.FieldTypes.EntitySelect)
            .Select(a => TryParseEntityId(a.Value))
            .Where(id => id.HasValue)
            .Select(id => id.Value)
            .Distinct()
            .ToList();

        if (entityIds.Count == 0) return new Dictionary<int, string>();

        var rows = await sdkDbContext.EntityItems
            .AsNoTracking()
            .Where(x => entityIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync();

        return rows
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    private static int? TryParseEntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null") return null;
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    /// <summary>
    /// Image REFERENCES for the group's cases. No bytes, ever — the response
    /// carries ids, the derived display name and a geo link, and #1168 fetches the
    /// pixels.
    ///
    /// <para>
    /// Filtered on the template's PICTURE field ids rather than on
    /// <c>Field.FieldTypeId</c>, which is what the shipped query does: that form
    /// forces a join to <c>Fields</c> and leaves the planner with no indexed entry
    /// point on <c>FieldValues</c>, i.e. the same full scan as §2.2. The picture
    /// field ids are already known from the schema. A template with no picture
    /// field issues no query at all.
    /// </para>
    /// </summary>
    private async Task LoadImages(TemplateSchema schema, List<int> caseIds, TemplateProjection projection)
    {
        if (schema.PictureFieldIds.Count == 0) return;

        var pictureFieldIds = schema.PictureFieldIds;
        var images = await sdkDbContext.FieldValues
            .AsNoTracking()
            // Same shape as LoadAnswers: FieldId first, so IX_field_values_field_id
            // is usable; CaseId alone would be a full scan.
            .Where(x => x.FieldId.HasValue && pictureFieldIds.Contains(x.FieldId.Value)
                        && x.CaseId.HasValue && caseIds.Contains(x.CaseId.Value)
                        && x.UploadedDataId != null
                        && x.WorkflowState != Constants.WorkflowStates.Removed
                        // The navigation must EXIST, not merely be non-removed. EF
                        // Core preserves C# null semantics, so the WorkflowState
                        // predicate below is emitted as
                        // `(u.workflow_state <> 'removed' OR u.workflow_state IS NULL)`
                        // over a LEFT JOIN — which a DANGLING UploadedDataId (a FK
                        // pointing at a row that no longer exists) passes. Without
                        // this the row would materialise and the projection would
                        // push a NULL into a non-nullable int.
                        && x.UploadedData != null
                        && x.UploadedData.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(x => x.CaseId).ThenBy(x => x.Id)
            .Select(x => new ImageRow
            {
                Id = x.Id,
                CaseId = x.CaseId.Value,
                Latitude = x.Latitude,
                Longitude = x.Longitude,
                // The FK already on the FieldValue, never x.UploadedData.Id: the
                // navigation is a LEFT JOIN and can materialise NULL into this
                // non-nullable int (see the Where above).
                UploadedDataId = x.UploadedDataId.Value,
                Checksum = x.UploadedData.Checksum,
                Extension = x.UploadedData.Extension,
                StoredFileName = x.UploadedData.FileName
            })
            .ToListAsync();

        foreach (var image in images)
        {
            var model = new ComplianceReportImageModel
            {
                FieldValueId = image.Id,
                UploadedDataId = image.UploadedDataId,
                // DERIVED, never stored. The stored FileName is only an existence
                // check — exactly as BackendConfigurationReportService.cs:741-744
                // uses it. 700 is the SDK's baked-in thumbnail width.
                FileName = string.IsNullOrEmpty(image.StoredFileName)
                    ? null
                    : $"{image.UploadedDataId}_700_{image.Checksum}{image.Extension}",
                // The 300px derivative, written to S3 by the same resize+crop
                // pass as the 700px one (EventsGrpcService.cs:2861-2862,
                // BackendConfigurationTaskManagementService.cs:526-527). Same
                // existence check, so it is null exactly when FileName is.
                ThumbnailFileName = string.IsNullOrEmpty(image.StoredFileName)
                    ? null
                    : $"{image.UploadedDataId}_300_{image.Checksum}{image.Extension}",
                GeoLink = !string.IsNullOrEmpty(image.Latitude) && !string.IsNullOrEmpty(image.Longitude)
                    ? $"https://www.google.com/maps/place/{image.Latitude},{image.Longitude}"
                    : null
            };

            if (!projection.ImagesByCaseId.TryGetValue(image.CaseId, out var list))
            {
                list = [];
                projection.ImagesByCaseId[image.CaseId] = list;
            }

            list.Add(model);
        }
    }

    // ==================================================================
    // Per-type rendering (§5)
    // ==================================================================

    /// <summary>
    /// Renders one raw <c>FieldValue.Value</c> for display, or returns
    /// <c>null</c> to mean "no cell".
    ///
    /// <para>
    /// Returning <c>null</c> rather than <c>""</c> is the whole point: the caller
    /// omits the key, so #1167 renders its empty glyph from the ABSENCE of a key
    /// and never from a blank string that could equally be a real blank answer.
    /// </para>
    /// </summary>
    internal static string Render(
        string fieldType,
        int fieldId,
        string rawValue,
        Dictionary<(int FieldId, string Key), string> optionLabels,
        Dictionary<int, string> entityNames)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        var value = rawValue.Trim();

        switch (fieldType)
        {
            // Stores a FieldOptions.Key, never a label.
            case Constants.FieldTypes.SingleSelect:
                return optionLabels.GetValueOrDefault((fieldId, value));

            // Pipe-joined option keys ("1|3"). Live data also holds comma-joined
            // legacy values ("0,1") and bare "0"; those resolve to nothing, and an
            // empty cell is better than echoing "0,1" at the user.
            case Constants.FieldTypes.MultiSelect:
            {
                var labels = value
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(key => optionLabels.GetValueOrDefault((fieldId, key.Trim())))
                    .Where(label => !string.IsNullOrEmpty(label))
                    .ToList();
                return labels.Count == 0 ? null : string.Join(", ", labels);
            }

            // An EntityItems.Id as a string — or the literal "null".
            case Constants.FieldTypes.EntitySearch:
            case Constants.FieldTypes.EntitySelect:
            {
                var entityId = TryParseEntityId(value);
                return entityId.HasValue ? entityNames.GetValueOrDefault(entityId.Value) : null;
            }

            // "checked" / "unchecked", plus dirty "true" / "false". The CANONICAL
            // token is emitted and #1167 localises it; anything else is not a
            // checkbox state and gets no cell.
            case Constants.FieldTypes.CheckBox:
                return value.ToLowerInvariant() switch
                {
                    "true" or "checked" => "checked",
                    "false" or "unchecked" => "unchecked",
                    _ => null
                };

            // Comma decimals exist in the data; emit invariant, as the shipped
            // report does at BackendConfigurationReportService.cs:925-931.
            case Constants.FieldTypes.Number:
            case Constants.FieldTypes.NumberStepper:
                return value.Replace(",", ".");

            // Already yyyy-MM-dd. Not reformatted server-side.
            case Constants.FieldTypes.Date:
                return value;

            // FOUR pipe-separated parts: start|stop|state|elapsed_ms. Only the
            // elapsed duration is useful in a table, so the cell is that duration
            // as H:mm:ss and the raw value is dropped. A value that is not in this
            // shape gets no cell rather than leaking "…UTC|…UTC|paused|38000".
            case Constants.FieldTypes.Timer:
            {
                var parts = value.Split('|');
                if (parts.Length < 4) return null;
                if (!long.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var elapsedMs) || elapsedMs < 0)
                {
                    return null;
                }

                // The UPPER bound matters as much as the lower one. Any 16-to-19
                // digit junk value parses happily as a long and then makes
                // TimeSpan.FromMilliseconds THROW — and that throw escapes Render,
                // LoadAnswers and ProjectAsync all the way to EformColumns' outer
                // catch, so ONE dirty cell would fail the WHOLE report (every tag,
                // every template) with Success = false. Timer is exactly the type
                // #1160 finding 4 flags as carrying dirty values in the wild. Out of
                // range is just another malformed value: no cell, like every other
                // path here.
                if (elapsedMs > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond) return null;

                var span = TimeSpan.FromMilliseconds(elapsedMs);
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                    (int)span.TotalHours, span.Minutes, span.Seconds);
            }

            // Free text, and any answerable type added to the SDK after this was
            // written: pass the raw value through rather than swallow it.
            case Constants.FieldTypes.Text:
            case Constants.FieldTypes.Comment:
            default:
                return value;
        }
    }

    // ==================================================================
    // Shapes
    // ==================================================================

    /// <summary>The derived, cached schema of one template.</summary>
    internal sealed class TemplateSchema
    {
        public int CheckListId { get; init; }
        public string CheckListName { get; set; }
        public List<ComplianceReportColumnModel> Columns { get; } = [];

        /// <summary>
        /// True when <c>Advanced_TemplateFieldReadAll</c> THREW and the column set is
        /// empty because derivation failed — not because the template has no
        /// answerable fields. Surfaced on
        /// <c>ComplianceReportTemplateGroupModel.SchemaUnavailable</c>.
        /// </summary>
        public bool SchemaUnavailable { get; set; }

        /// <summary>Answerable field id → SDK field type. Excluded types, picture
        /// fields and fields with no type at all are absent, which is what makes
        /// "no column ⇒ no cell" true by construction. Every value present is a
        /// non-blank type, so a column always has something to render with.</summary>
        public Dictionary<int, string> FieldTypeById { get; } = new();

        /// <summary>Picture field ids — excluded from cells, used for §6's images.</summary>
        public List<int> PictureFieldIds { get; } = [];
    }

    /// <summary>What one template group's bulk load produced.</summary>
    internal sealed class TemplateProjection
    {
        public TemplateSchema Schema { get; init; }
        public Dictionary<int, Dictionary<string, string>> CellsByCaseId { get; } = new();
        public Dictionary<int, List<ComplianceReportImageModel>> ImagesByCaseId { get; } = new();
    }

    /// <summary>The explicit answer projection — never <c>SELECT *</c>.
    /// internal, not private: EF Core materialises it from a projection, and a
    /// compiled expression tree cannot construct a private nested type.</summary>
    internal sealed class AnswerRow
    {
        public int Id { get; init; }
        public int CaseId { get; init; }
        public int FieldId { get; init; }
        public string Value { get; init; }
    }

    /// <summary>The explicit image projection. internal for the same reason as
    /// <see cref="AnswerRow"/>.</summary>
    internal sealed class ImageRow
    {
        public int Id { get; init; }
        public int CaseId { get; init; }
        public string Latitude { get; init; }
        public string Longitude { get; init; }
        public int UploadedDataId { get; init; }
        public string Checksum { get; init; }
        public string Extension { get; init; }
        public string StoredFileName { get; init; }
    }
}
