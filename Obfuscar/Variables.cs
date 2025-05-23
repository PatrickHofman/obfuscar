#region Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>

/// <copyright>
/// Copyright (c) 2007 Ryan Williams <drcforbin@gmail.com>
/// 
/// Permission is hereby granted, free of charge, to any person obtaining a copy
/// of this software and associated documentation files (the "Software"), to deal
/// in the Software without restriction, including without limitation the rights
/// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
/// copies of the Software, and to permit persons to whom the Software is
/// furnished to do so, subject to the following conditions:
/// 
/// The above copyright notice and this permission notice shall be included in
/// all copies or substantial portions of the Software.
/// 
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
/// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
/// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
/// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
/// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
/// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
/// THE SOFTWARE.
/// </copyright>

#endregion

using System.Collections.Generic;

namespace Obfuscar
{
    class Variables
    {
        readonly Dictionary<string, string> vars = new Dictionary<string, string>();

        readonly System.Text.RegularExpressions.Regex re =
            new System.Text.RegularExpressions.Regex(@"\$\(([^)]+)\)");

        public void Add(string name, string value)
        {
            vars[name] = value;
        }

        public void Remove(string name)
        {
            vars.Remove(name);
        }

        public string GetValue(string name, string def)
        {
            string value;
            return this.Replace(vars.TryGetValue(name, out value) ? value : def);
        }

        public string Replace(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            System.Text.StringBuilder formatted = new System.Text.StringBuilder();

            int lastMatch = 0;

            string variable;
            string replacement;
            foreach (System.Text.RegularExpressions.Match m in re.Matches(str))
            {
                formatted.Append(str.Substring(lastMatch, m.Index - lastMatch));

                variable = m.Groups[1].Value;
                if (vars.TryGetValue(variable, out replacement))
                    formatted.Append(this.Replace(replacement));
                else
                    throw new ObfuscarException("Unable to replace variable:  " + variable);

                lastMatch = m.Index + m.Length;
            }

            formatted.Append(str.Substring(lastMatch));

            return formatted.ToString();
        }
    }
}
