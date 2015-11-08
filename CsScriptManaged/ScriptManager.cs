﻿using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CsScriptManaged
{
    class ScriptManager
    {
        private const string AutoGeneratedNamespace = "AutoGeneratedNamespace";
        private const string AutoGeneratedClassName = "AutoGeneratedClassName";
        private const string AutoGeneratedScriptFunctionName = "ScriptFunction";
        private const string CodeBlockComments = @"/\*(.*?)\*/";
        private const string CodeLineComments = @"//(.*?)\r?\n";
        private const string CodeStrings = @"""((\\[^\n]|[^""\n])*)""";
        private const string CodeVerbatimStrings = @"@(""[^""]*"")+";
        private const string CodeImports = "import (([a-zA-Z][:])?([^\\/:*<>|;\"]+[\\/])*[^\\/:*<>|;\"]+);";
        private const string CodeUsings = "using ([^\";]+);";
        private static readonly Regex RegexRemoveComments = new Regex(CodeBlockComments + "|" + CodeLineComments + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RegexExtractImports = new Regex(CodeImports + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RegexExtractUsings = new Regex(CodeUsings + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);

        internal List<string> SearchFolders { get; } = new List<string>();

        internal void Execute(string path, string[] args)
        {
            string code = LoadCode(path);

            using (CSharpCodeProvider provider = new CSharpCodeProvider())
            {
                var compilerParameters = new CompilerParameters()
                {
                    GenerateInMemory = true,
                    IncludeDebugInformation = true,
                };

                compilerParameters.ReferencedAssemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray());
                CompilerResults results = provider.CompileAssemblyFromSource(compilerParameters, code);

                if (results.Errors.Count > 0)
                    throw new Exception(string.Join("\n", results.Errors.Cast<CompilerError>()));

                var myClass = results.CompiledAssembly.GetType(AutoGeneratedNamespace + "." + AutoGeneratedClassName);
                var method = myClass.GetMethod(AutoGeneratedScriptFunctionName);
                var obj = Activator.CreateInstance(myClass);

                method.Invoke(obj, new object[] { args });
            }
        }

        private string GetFullPath(string path, string parentPath)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            if (File.Exists(parentPath))
            {
                parentPath = Path.GetDirectoryName(parentPath);
            }

            string newPath = Path.Combine(parentPath, path);

            if (File.Exists(newPath))
            {
                return newPath;
            }

            foreach (string folder in SearchFolders)
            {
                newPath = Path.Combine(folder, path);
                if (File.Exists(newPath))
                {
                    return newPath;
                }
            }

            return path;
        }

        private string LoadCode(string path)
        {
            HashSet<string> loadedScripts = new HashSet<string>();
            HashSet<string> usings = new HashSet<string>();
            HashSet<string> imports = new HashSet<string>();
            StringBuilder codeBuilder = new StringBuilder();
            StringBuilder functions = new StringBuilder();
            string fullPath = GetFullPath(path, Directory.GetCurrentDirectory());
            string scriptCode = ImportFile(path, usings, imports);

            loadedScripts.Add(path);
            while (imports.Count > 0)
            {
                HashSet<string> newImports = new HashSet<string>();

                foreach (string import in imports)
                {
                    if (!loadedScripts.Contains(import))
                    {
                        string code = ImportFile(import, usings, newImports);

                        functions.AppendLine(code);
                        loadedScripts.Add(import);
                    }
                }

                imports = newImports;
            }

            // Generate C# code
            foreach (var u in usings.OrderBy(a => a))
            {
                codeBuilder.Append("using ");
                codeBuilder.Append(u);
                codeBuilder.AppendLine(";");
            }

            codeBuilder.Append("namespace ");
            codeBuilder.AppendLine(AutoGeneratedNamespace);
            codeBuilder.AppendLine("{");
            codeBuilder.Append("public class ");
            codeBuilder.Append(AutoGeneratedClassName);
            codeBuilder.AppendLine(" : CsScripts.ScriptBase");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine(functions.ToString());
            codeBuilder.Append("public void ");
            codeBuilder.Append(AutoGeneratedScriptFunctionName);
            codeBuilder.AppendLine("(string[] args)");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine(scriptCode);
            codeBuilder.AppendLine("}");
            codeBuilder.AppendLine("}");
            codeBuilder.AppendLine("}");
            return codeBuilder.ToString();
        }

        private string ImportFile(string path, ICollection<string> usings, ICollection<string> imports)
        {
            string code = File.ReadAllText(path);
            HashSet<string> localImports = new HashSet<string>();

            code = RemoveComments(code);
            code = ExtractImports(code, localImports);
            code = ExtractUsings(code, usings);
            foreach (string import in localImports)
            {
                imports.Add(GetFullPath(import, path));
            }

            return code;
        }

        private static string RemoveComments(string code)
        {
            return RegexRemoveComments.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("/*") || me.Value.StartsWith("//"))
                        return me.Value.StartsWith("//") ? Environment.NewLine : "";
                    return me.Value;
                });
        }

        private static string ExtractImports(string code, ICollection<string> imports)
        {
            return RegexExtractImports.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("import"))
                    {
                        imports.Add(me.Groups[1].Value);
                        return Environment.NewLine;
                    }

                    return me.Value;
                });
        }

        private static string ExtractUsings(string code, ICollection<string> usings)
        {
            return RegexExtractUsings.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("using"))
                    {
                        usings.Add(me.Groups[1].Value);
                        return Environment.NewLine;
                    }

                    return me.Value;
                });
        }
    }
}