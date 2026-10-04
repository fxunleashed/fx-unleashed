using Acornima;
using Acornima.Ast;
using System;
using System.Collections.Generic;
using System.Linq;

namespace User.FXProRpmSync
{
    /// <summary>
    /// The only JavaScript a library dash may carry: a "checked script". A dash formula starting with "js:" is real code that
    /// SimHub runs, so a downloaded one is parsed (with SimHub's own parser, Acornima) and allowed only when every part of
    /// it is on this short list. Anything else is refused, never judged.
    ///  - statements: if / else, blocks, return, var / let / const of plain names
    ///  - values: numbers, short texts, true, false, null, undefined, its own variables, root.name (the dash's memory)
    ///  - operators: + - * / %, comparisons, == and !=, && || ??, ! - +, a ? b : c, = += -= onto root.name or its own variable
    ///  - calls: $prop('Some.Property') with the name written out (never built from other values), and Math.abs / min / max / round ...
    /// No loops, no functions, no new, no this, no eval, no [ ], no template texts, no regular expressions, no constructor or
    /// __proto__ and nothing else of SimHub or .NET. The same rules are in the library repo (tools/script_rules.mjs, SCRIPTS.md)
    /// and share their test cases (tools/UsbTest/script-vectors.json); this copy is the one that counts, because it runs in the
    /// plugin when something is installed.
    /// </summary>
    public static class ScriptCheck
    {
        public const int MaxChars = 2000, MaxNodes = 400, MaxDepth = 16, MaxScriptsPerDash = 16, MaxTextChars = 200;

        private static readonly HashSet<string> MathOk = new HashSet<string>
        {
            "abs", "min", "max", "round", "floor", "ceil", "trunc", "sign", "sqrt", "pow", "log", "exp", "sin", "cos", "tan", "atan", "atan2", "PI", "E",
        };
        private static readonly HashSet<string> BadNames = new HashSet<string>
        {
            "constructor", "__proto__", "prototype", "__defineGetter__", "__defineSetter__", "__lookupGetter__", "__lookupSetter__",
        };
        private static readonly HashSet<string> Globals = new HashSet<string> { "root", "undefined", "NaN", "Infinity" };

        /// <summary>The problems with a script (the text after "js:"); empty = it may be used.</summary>
        public static List<string> Check(string source)
        {
            var problems = new List<string>();
            try { Run(source ?? "", problems); }
            catch (Exception ex) { problems.Add("couldn't be checked (" + ex.GetType().Name + "): refused to be safe"); }
            return problems;
        }

        /// <summary>Every js: formula of a dash, checked. (Problems say which one.)</summary>
        public static List<string> CheckDash(DashDefinition d)
        {
            var problems = new List<string>();
            var scripts = d.Bindings.Where(b => b != null && b.TrimStart().StartsWith("js:", StringComparison.OrdinalIgnoreCase)).Select(b => b.TrimStart().Substring(3)).Distinct().ToList();
            if (scripts.Count > MaxScriptsPerDash) problems.Add($"{scripts.Count} scripts (at most {MaxScriptsPerDash})");
            for (int i = 0; i < scripts.Count && i < MaxScriptsPerDash; i++)
                foreach (var p in Check(scripts[i])) problems.Add($"script {i + 1} {p}");
            return problems;
        }

        private static void Run(string source, List<string> problems)
        {
            if (source.Length > MaxChars) { problems.Add($"is {source.Length} characters (at most {MaxChars})"); return; }
            Script program;
            try { program = new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source); }
            catch (ParseErrorException ex) { problems.Add("isn't valid JavaScript: " + ex.Message); return; }

            var locals = new HashSet<string>();
            var seen = new HashSet<string>();
            int nodes = 0;
            bool tooBig = false;
            void Add(string p) { if (seen.Add(p) && problems.Count < 8) problems.Add(p); }
            bool Count(int depth)
            {
                if (++nodes > MaxNodes || depth > MaxDepth) { if (!tooBig) { tooBig = true; Add("is too long or too deeply nested"); } return false; }
                return true;
            }
            bool PlainName(string name) => !string.IsNullOrEmpty(name) && !BadNames.Contains(name) && !Globals.Contains(name) && name != "Math" && name != "$prop"
                                           && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');
            string Describe(Node n)
            {
                string t = n.GetType().Name;
                return t.EndsWith("Statement") ? "a " + t.Replace("Statement", "").ToLowerInvariant() + " statement" : t.EndsWith("Expression") ? "a " + t.Replace("Expression", "").ToLowerInvariant() + " expression" : t;
            }
            // root.name, with a name that can't reach the language's own machinery
            bool RootMember(Node n) => n is MemberExpression m && !m.Computed && !m.Optional && m.Object is Identifier o && o.Name == "root" && m.Property is Identifier p && !BadNames.Contains(p.Name);

            void Statement(Node n, int depth)
            {
                if (!Count(depth)) return;
                switch (n)
                {
                    case Directive _: break; // "use strict"
                    case ExpressionStatement es: Expression(es.Expression, depth + 1); break;
                    case IfStatement i:
                        Expression(i.Test, depth + 1); Statement(i.Consequent, depth + 1);
                        if (i.Alternate != null) Statement(i.Alternate, depth + 1);
                        break;
                    case BlockStatement b: foreach (var s in b.Body) Statement(s, depth + 1); break;
                    case ReturnStatement r: if (r.Argument != null) Expression(r.Argument, depth + 1); break;
                    case EmptyStatement _: break;
                    case VariableDeclaration v:
                        foreach (var d in v.Declarations)
                        {
                            if (d.Id is Identifier id && PlainName(id.Name)) { locals.Add(id.Name); if (d.Init != null) Expression(d.Init, depth + 1); }
                            else Add("declares something that isn't a plain variable name");
                        }
                        break;
                    default: Add(Describe(n) + " isn't allowed"); break;
                }
            }

            void Expression(Node n, int depth)
            {
                if (!Count(depth)) return;
                switch (n)
                {
                    case NumericLiteral _: case BooleanLiteral _: case NullLiteral _: break;
                    case StringLiteral s: if (s.Value.Length > MaxTextChars) Add($"has a text longer than {MaxTextChars} characters"); break;
                    case Identifier id:
                        if (!locals.Contains(id.Name) && !Globals.Contains(id.Name)) Add($"uses \"{id.Name}\": only root, $prop(...), Math and its own variables are available");
                        break;
                    case MemberExpression m:
                        if (RootMember(m)) break;
                        if (!m.Computed && !m.Optional && m.Object is Identifier mo && mo.Name == "Math" && m.Property is Identifier mp && MathOk.Contains(mp.Name)) break;
                        Add("uses a property of something other than root or Math");
                        break;
                    case CallExpression c:
                        if (c.Optional) { Add("uses ?.()"); break; }
                        if (c.Callee is Identifier f && f.Name == "$prop")
                        {
                            if (c.Arguments.Count < 1 || c.Arguments.Count > 2 || !(c.Arguments[0] is StringLiteral))
                                Add("calls $prop with something other than a written-out property name");
                            else if (c.Arguments.Count == 2) Expression(c.Arguments[1], depth + 1);
                            Count(depth + 1);
                        }
                        else if (c.Callee is MemberExpression cm && !cm.Computed && !cm.Optional && cm.Object is Identifier co && co.Name == "Math" && cm.Property is Identifier cp && MathOk.Contains(cp.Name))
                            foreach (var a in c.Arguments) Expression(a, depth + 1);
                        else Add($"calls {(c.Callee is Identifier ci ? ci.Name + "(...)" : "something else")}: only $prop('...') and Math.name(...) may be called");
                        break;
                    case UnaryExpression u:
                        if (u.Operator == Operator.LogicalNot || u.Operator == Operator.UnaryNegation || u.Operator == Operator.UnaryPlus) Expression(u.Argument, depth + 1);
                        else Add("uses the operator " + Describe(u).Replace("a unary expression", u.Operator.ToString()));
                        break;
                    case LogicalExpression l: Expression(l.Left, depth + 1); Expression(l.Right, depth + 1); break;
                    case BinaryExpression b:
                        switch (b.Operator)
                        {
                            case Operator.Addition: case Operator.Subtraction: case Operator.Multiplication: case Operator.Division: case Operator.Remainder:
                            case Operator.Equality: case Operator.Inequality: case Operator.StrictEquality: case Operator.StrictInequality:
                            case Operator.LessThan: case Operator.LessThanOrEqual: case Operator.GreaterThan: case Operator.GreaterThanOrEqual:
                                Expression(b.Left, depth + 1); Expression(b.Right, depth + 1); break;
                            default: Add("uses the operator " + b.Operator); break;
                        }
                        break;
                    case ConditionalExpression q: Expression(q.Test, depth + 1); Expression(q.Consequent, depth + 1); Expression(q.Alternate, depth + 1); break;
                    case AssignmentExpression a:
                        if (a.Operator != Operator.Assignment && a.Operator != Operator.AdditionAssignment && a.Operator != Operator.SubtractionAssignment) Add("uses the assignment " + a.Operator);
                        if (!(RootMember(a.Left) || (a.Left is Identifier li && locals.Contains(li.Name)))) Add("assigns to something other than root.name or its own variable");
                        Expression(a.Right, depth + 1);
                        break;
                    default: Add(Describe(n) + " isn't allowed"); break;
                }
            }

            foreach (var s in program.Body) Statement(s, 1);
        }
    }
}
