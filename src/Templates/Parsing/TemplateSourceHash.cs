using System;
using System.Security.Cryptography;
using System.Text;

namespace CL.WebLogic.Templates.Parsing;

/// <summary>
/// Canonical template source hash, shared verbatim between the source generator
/// (build time, netstandard2.0) and the theme engine (render time, net10) via
/// linked compilation — guaranteeing both sides compute the identical value.
/// Normalizes a leading BOM and CRLF→LF first so end-of-line conversion between
/// checkout and runtime never poisons the compiled-template gate.
/// </summary>
public static class TemplateSourceHash
{
    public static string Compute(string source)
    {
        var text = source ?? string.Empty;
        if (text.Length > 0 && text[0] == '﻿')
            text = text.Substring(1);
        text = text.Replace("\r\n", "\n");

        var bytes = Encoding.UTF8.GetBytes(text);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);

        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
