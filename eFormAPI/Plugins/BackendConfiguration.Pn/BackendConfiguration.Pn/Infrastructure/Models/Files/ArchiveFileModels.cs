#nullable enable
using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Files;

/// <summary>PUT files/folder: move documents into one folder (D11, D17).</summary>
public class FilesMoveModel
{
    public List<int> FileIds { get; set; } = [];
    public int FolderId { get; set; }
}

/// <summary><see cref="AccessChanged"/> feeds the D11 message ("3 dokumenter kan nu læses af alle").</summary>
public class FilesMoveResultModel
{
    public int Moved { get; set; }
    public int AccessChanged { get; set; }
}

/// <summary>PUT files/{id}/date: a date approves it (approving a suggestion = sending the suggested date); null clears it.</summary>
public class FileDateModel
{
    public DateTime? Date { get; set; }
}

/// <summary>PUT files/access (admin): own groups per document; <see cref="OwnAccess"/> false = use the folder's access.</summary>
public class FilesAccessModel
{
    public List<int> FileIds { get; set; } = [];
    public bool OwnAccess { get; set; }
    public List<int> TagIds { get; set; } = [];
}
