﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Jurassic;
using Jurassic.Compiler;
using Jurassic.Library;
using Sparrow.Extensions;
using Raven.Client.Exceptions.Documents.Patching;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.Patch
{
    public class ScriptRunner
    {
        private readonly DocumentDatabase _db;
        private readonly bool _enableClr;
        private readonly List<CompiledScript> _scripts = new List<CompiledScript>();
        public readonly List<string> ScriptsSource = new List<string>();

        public void AddScript(string script)
        {
            CompiledScript compiledScript;
            try
            {
                compiledScript = CompiledScript.Compile(new StringScriptSource(script),
                    new CompilerOptions
                    {
                        EmitOnLoopIteration = SingleRun.GetUselessOnStateLoopIterationInstanceForCodeGenerationOnly()
                    });
            }
            catch (Exception e)
            {
                throw new JavaScriptParseException("Failed to parse:" + Environment.NewLine + script, e);
            }
            ScriptsSource.Add(script);
            _scripts.Add(compiledScript);
        }

        public void AddScript(CompiledScript compiledScript)
        {
            _scripts.Add(compiledScript);
        }

        public class SingleRun
        {
            public List<string> DebugOutput;
            public bool DebugMode;
            public bool PutOrDeleteCalled;
            public PatchDebugActions DebugActions;

            public override string ToString()
            {
                return string.Join(Environment.NewLine, _runner.ScriptsSource);
            }

            private SingleRun()
            {
                // here just to get an instance that jurrasic
                // can use
            }

            public SingleRun(DocumentDatabase database, ScriptRunner runner)
            {
                _database = database;
                _runner = runner;
                ScriptEngine = new ScriptEngine
                {
                    RecursionDepthLimit = 64,
                    OnLoopIterationCall = OnStateLoopIteration,
                    EnableExposedClrTypes = runner._enableClr
                };
                ScriptEngine.SetGlobalFunction("output", (Action<object>)OutputDebug);

                ScriptEngine.SetGlobalFunction("load", (Func<object, object>)LoadDocument);
                ScriptEngine.SetGlobalFunction("del", (Func<object, object, bool>)DeleteDocument);
                ScriptEngine.SetGlobalFunction("put", (Func<object, object, object, string>)PutDocument);

                ScriptEngine.SetGlobalFunction("id", (Func<object, string>)GetDocumentId);
                ScriptEngine.SetGlobalFunction("lastModified", (Func<object, string>)GetLastModified);
            }


            private string GetLastModified(object arg)
            {
                if (arg is BlittableObjectInstance doc)
                    return doc.LastModified?.GetDefaultRavenFormat();
                return null;
            }

            private void OutputDebug(object obj)
            {
                if (DebugMode == false)
                    return;

                if (obj is string str)
                {
                    DebugOutput.Add(str);
                }
                else if (obj is ObjectInstance json)
                {
                    var globalValue = ScriptEngine.GetGlobalValue<ObjectInstance>("JSON");
                    var stringified = (string)globalValue.CallMemberFunction("stringify", json);
                    DebugOutput.Add(stringified);
                }
                else if (obj == Undefined.Value)
                {
                    DebugOutput.Add("undefined");
                }
                else if (obj == null || obj == Null.Value)
                {
                    DebugOutput.Add("null");
                }
                else
                {
                    DebugOutput.Add(obj.ToString());
                }
            }

            private string GetStringFromObject(object o)
            {
                if (o == null || o == Null.Value || o == Undefined.Value)
                    return null;
                return o.ToString();
            }

            public string PutDocument(object id, object document, object changeVector)
            {
                var strId = GetStringFromObject(id);
                var strCv = GetStringFromObject(changeVector);
                PutOrDeleteCalled = true;
                AssertValidDatabaseContext();
                AssertNotReadOnly();
                var objectInstance = document as ObjectInstance;
                if (objectInstance == null)
                {
                    AssertValidDocumentObject(strId);
                    return null;//never hit
                }
                AssertValidDatabaseContext();
               
                if (DebugMode)
                {
                    DebugActions.PutDocument.Add(strId);
                }

                using (var reader = JurrasicBlittableBridge.Translate(_context, objectInstance,
                    BlittableJsonDocumentBuilder.UsageMode.ToDisk))
                {
                    var put = _database.DocumentsStorage.Put(_context, strId, _context.GetLazyString(strCv), reader);
                    return put.Id;
                }
            }

            public bool DeleteDocument(object id, object changeVector)
            {
                var strId = GetStringFromObject(id);
                var strCv = GetStringFromObject(changeVector);

                PutOrDeleteCalled = true;
                AssertValidId(strId);
                AssertValidDatabaseContext();
                AssertNotReadOnly();
                if (DebugMode)
                {
                    DebugActions.DeleteDocument.Add(strId);
                }
                var result = _database.DocumentsStorage.Delete(_context, strId, strCv);
                return result != null;

            }

            private void AssertValidId(string strId)
            {
                if(string.IsNullOrEmpty(strId))
                    throw new InvalidOperationException("This operation required a valid document id");
            }

            private void AssertNotReadOnly()
            {
                if (ReadOnly)
                    throw new InvalidOperationException("Cannot make modifications in readonly context");
            }

            private static void AssertValidDocumentObject(string id)
            {
                throw new InvalidOperationException(
                    $"Created document must be a valid object which is not null or empty. Document ID: '{id}'.");
            }

            private void AssertValidDatabaseContext()
            {
                if (_context == null)
                    throw new InvalidOperationException("Unable to put documents when this instance is not attached to a database operation");
            }

            private string GetDocumentId(object arg)
            {
                if (arg is BlittableObjectInstance doc)
                    return doc.DocumentId;
                return null;
            }

            private object LoadDocument(object id)
            {
                var strId = GetStringFromObject(id);
                AssertValidId(strId);

                AssertValidDatabaseContext();

                if (DebugMode)
                {
                    DebugActions.LoadDocument.Add(strId);
                }
                var document = _database.DocumentsStorage.Get(_context, strId);
                if (document == null)
                    return Null.Value;
                return new BlittableObjectInstance(ScriptEngine, document.Data, document.Id, document.LastModified);
            }

            public bool ReadOnly;
            private readonly DocumentDatabase _database;
            private readonly ScriptRunner _runner;
            private DocumentsOperationContext _context;

            public int MaxSteps;
            public int CurrentSteps;
            public readonly ScriptEngine ScriptEngine;

            private static void ThrowTooManyLoopIterations() =>
                throw new TimeoutException("The scripts has run for too long and was aborted by the server");

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnStateLoopIteration()
            {
                CurrentSteps++;
                if (CurrentSteps < MaxSteps)
                    return;
                ThrowTooManyLoopIterations();
            }

            public ScriptRunnerResult Run(DocumentsOperationContext ctx, string method, object[] args)
            {
                _context = ctx;
                if (DebugMode)
                {
                    if(DebugOutput == null)
                        DebugOutput = new List<string>();
                    if(DebugActions == null)
                        DebugActions = new PatchDebugActions();
                }
                PutOrDeleteCalled = false;
                CurrentSteps = 0;
                MaxSteps = 1000; // TODO: Maxim make me configurable
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = TranslateToJurrasic(ScriptEngine, args[i]);
                }
                var result = ScriptEngine.CallGlobalFunction(method, args);
                return new ScriptRunnerResult(result);
            }


#if DEBUG
            static readonly HashSet<Type> ExpectedTypes = new HashSet<Type>
            {
                typeof(int),
                typeof(long),
                typeof(double),
                typeof(bool),
                typeof(string),
            };
#endif

            private object TranslateToJurrasic(ScriptEngine engine, object o)
            {
                if (o is Document d)
                    return new BlittableObjectInstance(engine, d.Data, d.Id, d.LastModified);
                if (o is DocumentConflict dc)
                    return new BlittableObjectInstance(engine, dc.Doc, dc.Id, dc.LastModified);
                if (o is BlittableJsonReaderObject json)
                    return new BlittableObjectInstance(engine, json, null, null);
                if (o is BlittableJsonReaderArray array)
                    return BlittableObjectInstance.CreateArrayInstanceBasedOnBlittableArray(engine, array);
                if (o == null)
                    return Null.Value;
                if (o is long)
                    return BlittableObjectInstance.GetJurrasicNumber_TEMPORARY(o);
                if (o is List<object> l)
                {
                    var list = engine.Array.Construct();
                    for (int i = 0; i < l.Count; i++)
                    {
                        list.Push(TranslateToJurrasic(ScriptEngine, l[i]));
                    }
                    return list;
                }
                // for admin
                if (o is RavenServer || o is DocumentDatabase)
                {
                    AssertAdminScriptInstance();
                    return engine.Object.Construct(o);
                }
                if (o is ObjectInstance)
                {
                    return o;
                }
#if DEBUG
                Debug.Assert(ExpectedTypes.Contains(o.GetType()));
#endif
                return o;
            }

            private void AssertAdminScriptInstance()
            {
                if (_runner._enableClr == false)
                    throw new InvalidOperationException("Unable to run admin scripts using this instance of the script runner, the EnableClr is set to false");
            }

            public object CreateEmptyObject()
            {
                return ScriptEngine.Object.Construct();
            }

            internal static Action GetUselessOnStateLoopIterationInstanceForCodeGenerationOnly()
            {
                return new SingleRun().OnStateLoopIteration;
            }
        }

        public ScriptRunner(DocumentDatabase db, bool enableClr)
        {
            _db = db;
            _enableClr = enableClr;
        }

        private readonly ConcurrentQueue<SingleRun> _cache = new ConcurrentQueue<SingleRun>();

        public long Runs;

        public ReturnRun GetRunner(out SingleRun run)
        {
            if (_cache.TryDequeue(out run) == false)
            {
                run = new SingleRun(_db, this);
                foreach (var compiledScript in _scripts)
                {
                    compiledScript.Execute(run.ScriptEngine);
                }
            }
            Interlocked.Increment(ref Runs);
            return new ReturnRun(this, run);
        }

        public struct ReturnRun : IDisposable
        {
            private ScriptRunner _parent;
            private SingleRun _run;

            public ReturnRun(ScriptRunner parent, SingleRun run)
            {
                _parent = parent;
                _run = run;
            }

            public void Dispose()
            {
                if (_run == null)
                    return;
                _run.ReadOnly = false;
                _run.DebugMode = false;
                _run.DebugOutput?.Clear();
                _run.DebugActions?.Clear();
                _parent._cache.Enqueue(_run);
                _run = null;
                _parent = null;
            }
        }
    }
}
