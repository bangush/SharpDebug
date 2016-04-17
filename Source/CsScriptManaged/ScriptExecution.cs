﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CsScriptManaged
{
    /// <summary>
    /// Compiles and executes scripts
    /// </summary>
    internal class ScriptExecution : ScriptCompiler
    {
        /// <summary>
        /// Executes the specified script.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="args">The arguments.</param>
        internal void Execute(string path, params string[] args)
        {
            HashSet<string> referencedAssemblies = new HashSet<string>();
            string code = LoadCode(path, referencedAssemblies);

            // Compile the script
            var results = Compile(code, referencedAssemblies.ToArray());

            if (results.Errors.Length > 0)
            {
                throw new Exception(string.Join("\n", results.Errors.Select(e => e.FullMessage)));
            }

            // Extract metadata
            var metadataAssemblies = new List<Assembly>();

            metadataAssemblies.Add(results.CompiledAssembly);
            foreach (var referencedAssembly in referencedAssemblies)
            {
                metadataAssemblies.Add(Assembly.LoadFrom(referencedAssembly));
            }

            var metadata = ExtractMetadata(metadataAssemblies);

            Context.UserTypeMetadata = metadata;

            try
            {
                // Execute the script
                var myClass = results.CompiledAssembly.GetType(AutoGeneratedNamespace + "." + AutoGeneratedClassName);
                var method = myClass.GetMethod(AutoGeneratedScriptFunctionName);
                var obj = Activator.CreateInstance(myClass);

                method.Invoke(obj, new object[] { args });
            }
            finally
            {
                // Clear metadata cache
                Context.ClearMetadataCache();
            }
        }
    }
}