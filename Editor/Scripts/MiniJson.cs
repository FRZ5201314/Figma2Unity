using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FigmaUiImporter.Editor
{
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            return Parser.Parse(json);
        }

        private sealed class Parser
        {
            private const string WordBreak = "{}[],:\"";
            private readonly string _json;
            private int _index;

            private Parser(string json)
            {
                _json = json;
            }

            public static object Parse(string json)
            {
                return new Parser(json).ParseValue();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> table = new Dictionary<string, object>();
                NextChar();

                while (true)
                {
                    Token token = NextToken;
                    if (token == Token.None)
                    {
                        return null;
                    }

                    if (token == Token.CurlyClose)
                    {
                        return table;
                    }

                    if (token == Token.Comma)
                    {
                        NextChar();
                        continue;
                    }

                    string name = ParseString();
                    if (name == null)
                    {
                        return null;
                    }

                    if (NextToken != Token.Colon)
                    {
                        return null;
                    }

                    NextChar();
                    table[name] = ParseValue();
                }
            }

            private List<object> ParseArray()
            {
                List<object> array = new List<object>();
                NextChar();

                bool parsing = true;
                while (parsing)
                {
                    Token token = NextToken;
                    if (token == Token.None)
                    {
                        return null;
                    }

                    if (token == Token.SquaredClose)
                    {
                        break;
                    }

                    if (token == Token.Comma)
                    {
                        NextChar();
                        continue;
                    }

                    array.Add(ParseValue());
                }

                return array;
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case Token.String:
                        return ParseString();
                    case Token.Number:
                        return ParseNumber();
                    case Token.CurlyOpen:
                        return ParseObject();
                    case Token.SquaredOpen:
                        return ParseArray();
                    case Token.True:
                        NextWord();
                        return true;
                    case Token.False:
                        NextWord();
                        return false;
                    case Token.Null:
                        NextWord();
                        return null;
                    default:
                        return null;
                }
            }

            private string ParseString()
            {
                StringBuilder builder = new StringBuilder();
                NextChar();

                bool parsing = true;
                while (parsing && _index < _json.Length)
                {
                    char c = NextChar();
                    switch (c)
                    {
                        case '"':
                            parsing = false;
                            break;
                        case '\\':
                            if (_index == _json.Length)
                            {
                                break;
                            }

                            c = NextChar();
                            switch (c)
                            {
                                case '"':
                                case '\\':
                                case '/':
                                    builder.Append(c);
                                    break;
                                case 'b':
                                    builder.Append('\b');
                                    break;
                                case 'f':
                                    builder.Append('\f');
                                    break;
                                case 'n':
                                    builder.Append('\n');
                                    break;
                                case 'r':
                                    builder.Append('\r');
                                    break;
                                case 't':
                                    builder.Append('\t');
                                    break;
                                case 'u':
                                    if (_index + 4 <= _json.Length)
                                    {
                                        string hex = _json.Substring(_index, 4);
                                        ushort code;
                                        if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                        {
                                            builder.Append((char)code);
                                        }

                                        _index += 4;
                                    }
                                    break;
                            }
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                }

                return builder.ToString();
            }

            private object ParseNumber()
            {
                string number = NextWord();
                if (number.IndexOf(".", StringComparison.Ordinal) == -1
                    && number.IndexOf("e", StringComparison.OrdinalIgnoreCase) == -1)
                {
                    long parsedInt;
                    if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                    {
                        return parsedInt;
                    }
                }

                double parsedDouble;
                double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedDouble);
                return parsedDouble;
            }

            private void EatWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }

            private char PeekChar
            {
                get { return _json[_index]; }
            }

            private char NextChar()
            {
                return _json[_index++];
            }

            private string NextWord()
            {
                StringBuilder builder = new StringBuilder();
                while (_index < _json.Length && !IsWordBreak(PeekChar))
                {
                    builder.Append(NextChar());
                }

                return builder.ToString();
            }

            private Token NextToken
            {
                get
                {
                    EatWhitespace();
                    if (_index == _json.Length)
                    {
                        return Token.None;
                    }

                    switch (PeekChar)
                    {
                        case '{':
                            return Token.CurlyOpen;
                        case '}':
                            NextChar();
                            return Token.CurlyClose;
                        case '[':
                            return Token.SquaredOpen;
                        case ']':
                            NextChar();
                            return Token.SquaredClose;
                        case ',':
                            return Token.Comma;
                        case '"':
                            return Token.String;
                        case ':':
                            return Token.Colon;
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                        case '-':
                            return Token.Number;
                    }

                    string word = NextWord();
                    switch (word)
                    {
                        case "false":
                            return Token.False;
                        case "true":
                            return Token.True;
                        case "null":
                            return Token.Null;
                    }

                    return Token.None;
                }
            }

            private static bool IsWordBreak(char c)
            {
                return char.IsWhiteSpace(c) || WordBreak.IndexOf(c) != -1;
            }

            private enum Token
            {
                None,
                CurlyOpen,
                CurlyClose,
                SquaredOpen,
                SquaredClose,
                Colon,
                Comma,
                String,
                Number,
                True,
                False,
                Null
            }
        }
    }
}
