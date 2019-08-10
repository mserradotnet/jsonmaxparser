using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace MSerra.Net
{
    public class JsonMaxParser
    {
        private const string rootName = "$";
        private readonly char[] stringBuffer;
        private readonly JsonMaxParserOptions options;

        private bool error;
        private string errorMessage;

        public JsonMaxParser(JsonMaxParserOptions options = null)
        {
            this.options = options ?? new JsonMaxParserOptions()
            {
                FlattenHierarchy = false,
                FlatArrayNotation = FlatArrayNotation.Indexed,
                RootName = rootName,
                OptimisticParsing = true,
                ParseNumber = true,
                StringBufferSizeInBytes = 1024 * 8
            };
            this.stringBuffer = new char[this.options.StringBufferSizeInBytes];
        }

        public Dictionary<string, object> Parse(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return Parse(bytes, 0, bytes.Length);
        }

        public Dictionary<string, object> Parse(byte[] buffer, int offset, int length)
        {
            using var stream = new MemoryStream(buffer, offset, length);
            return Parse(stream);
        }

        public Dictionary<string, object> Parse(Stream jsonStream)
        {
            using var reader = new StreamReader(jsonStream);
            return Parse(reader);
        }

        public Dictionary<string, object> Parse(StreamReader reader)
        {
            error = false;
            errorMessage = string.Empty;
            var c = default(int);
            var initialContext = new Context();
            return Parse(reader, ref c, ref initialContext);
        }

        private Dictionary<string, object> Parse(StreamReader reader, ref int currentChar, ref Context currentContext)
        {
            bool noRead;
            var response = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (currentContext.IsNull) // We just started reading the JSON
            {
                currentContext = new Context() { FieldName = options.RootName };

                if (reader.EndOfStream) throw new ApplicationException("File is empty or malformed.");

                ReadAlphaNumOrNull(reader, ref currentChar, out noRead, out var value);
                if (error) throw new ApplicationException(errorMessage);
                if (!noRead)
                {
                    // We check that there was just a string or number or null and that JSON is not malformed
                    TrimNext(reader, ref currentChar);
                    if (!reader.EndOfStream) throw new ApplicationException("JSON string is malformed.");
                    response.TryAdd(options.RootName, value);
                    return response;
                }

                // To this point, either an object or array
                if (currentChar != '{' && currentChar != '[') throw new ApplicationException("Malformed JSON.");

                if (currentChar == '{') currentContext.IsInObject = true;
                if (currentChar == '[') currentContext.IsInArray = true;

                currentContext.Openings++;
            }

            // JSON is object or array
            while (!reader.EndOfStream)
            {
                if (currentContext.IsInObject) // Right after the first opening '{'
                {
                    ReadProperty(reader, ref currentChar, out var currentField);
                    if (error) throw new ApplicationException(errorMessage);
                    if (currentField == null)
                    {
                        currentChar = reader.Read();
                        return response;
                    }
                    var key = !string.IsNullOrWhiteSpace(currentContext.FieldName) && options.FlattenHierarchy ? $"{currentContext.FieldName}.{currentField}" : currentField;
                    ReadAlphaNumOrNull(reader, ref currentChar, out noRead, out var value);
                    if (error) throw new ApplicationException(errorMessage);
                    if (!noRead)
                    {
                        response.TryAdd(key, value);
                    }
                    else if (noRead && (currentChar == '{' || currentChar == '['))
                    {
                        var subContext = new Context()
                        {
                            FieldName = key,
                            Openings = 1,
                            IsInObject = currentChar == '{',
                            IsInArray = currentChar == '['
                        };
                        value = Parse(reader, ref currentChar, ref subContext);
                        if (options.FlattenHierarchy)
                        {
                            foreach (var kv in ((IDictionary<string, object>)value))
                                response.TryAdd(kv.Key, kv.Value);
                        }
                        else
                        {
                            response.TryAdd(key, value);
                        }
                    }
                    if (!reader.EndOfStream && currentChar != ',' && currentChar != '}' && currentChar != ']') throw new ApplicationException("Malformed JSON, bad field value ending.");
                    if (currentChar == '}')
                    {
                        TrimNext(reader, ref currentChar);
                        currentContext.Closings++;
                        return response;
                    }
                }
                else if (currentContext.IsInArray) // Right after the first opening '['
                {
                    var index = !options.FlattenHierarchy ?
                        $"{currentContext.ArrayIndex++}" :
                        (options.FlatArrayNotation == FlatArrayNotation.Indexed ? $"{currentContext.FieldName}[{currentContext.ArrayIndex++}]" : $"{currentContext.FieldName}.{currentContext.ArrayIndex++}");
                    var key = index;
                    ReadAlphaNumOrNull(reader, ref currentChar, out noRead, out var value);
                    if (error) throw new ApplicationException(errorMessage);
                    if (!noRead)
                    {
                        response.TryAdd(key, value);
                    }
                    else if (noRead && (currentChar == '{' || currentChar == '['))
                    {
                        var subContext = new Context()
                        {
                            FieldName = key,
                            Openings = 1,
                            IsInObject = currentChar == '{',
                            IsInArray = currentChar == '[',
                        };
                        value = Parse(reader, ref currentChar, ref subContext);
                        if (options.FlattenHierarchy)
                        {
                            foreach (var kv in ((IDictionary<string, object>)value))
                                response.TryAdd(kv.Key, kv.Value);
                        }
                        else
                        {
                            response.TryAdd(key, value);
                        }
                    }
                    if (!reader.EndOfStream && currentChar != ',' && currentChar != '}' && currentChar != ']') throw new ApplicationException("Malformed JSON, bad array value ending.");
                    if (currentChar == ']')
                    {
                        TrimNext(reader, ref currentChar);
                        currentContext.Closings++;
                        return response;
                    }
                }
            }

            return response;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadProperty(StreamReader reader, ref int currentChar, out string propertyName)
        {
            propertyName = string.Empty;
            TrimNext(reader, ref currentChar);
            if (currentChar == '}') return;
            if (currentChar != '"')
            {
                SetError("Error reading property.");
                return;
            }
            var count = 0;
            do
            {
                currentChar = reader.Read();
                if (currentChar != '"' && (currentChar < 'a' || currentChar > 'z') && (currentChar < 'A' || currentChar > 'Z') && (currentChar < '0' || currentChar > '9') && currentChar != '_')
                {
                    SetError($"Forbidden char detected in a field name : '{currentChar}'");
                    return;
                }
                if (currentChar != '"') stringBuffer[count++] = (char)currentChar;
            } while (!reader.EndOfStream && currentChar != '"');
            TrimNext(reader, ref currentChar);
            if (currentChar != ':')
            {
                SetError("Malformed JSON field, missing ':'.");
                return;
            }
            propertyName = new string(new ReadOnlySpan<char>(stringBuffer, 0, count));
        }

        // We expect to encounter a field (i.e. starting with ' or ")
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadAlphaNumOrNull(StreamReader reader, ref int currentChar, out bool noRead, out object value)
        {
            noRead = true;
            value = default(object);
            TrimNext(reader, ref currentChar);
            if (currentChar == '"' || currentChar == '\'')
            {
                int previousPreviousChar;
                int previousChar = currentChar;
                var stringDelimiter = currentChar;
                var count = 0;
                while (!reader.EndOfStream)
                {
                    previousPreviousChar = previousChar;
                    previousChar = currentChar;
                    currentChar = reader.Read();
                    if (currentChar == stringDelimiter && (previousChar != '\\' || previousPreviousChar == '\\')) break;
                    stringBuffer[count++] = (char)currentChar;
                }
                value = new string(new ReadOnlySpan<char>(stringBuffer, 0, count));
                if (currentChar != stringDelimiter)
                {
                    SetError($"Malformed string : [{value.ToString()}]");
                    return;
                }
                noRead = false;
                currentChar = reader.Read();
            }
            else if ((currentChar >= '0' && currentChar <= '9') || currentChar == '-')
            {
                var firstInt = currentChar;
                var count = 0;
                stringBuffer[count++] = (char)firstInt;
                var hadPoint = false;
                while (!reader.EndOfStream)
                {
                    currentChar = reader.Read();
                    if ((currentChar < '0' || currentChar > '9') && currentChar != '.') break;
                    if (currentChar == '.' && hadPoint)
                    {
                        SetError("JSON number is malformed");
                        return;
                    }
                    if (currentChar == '.' && !hadPoint) hadPoint = true;
                    stringBuffer[count++] = currentChar == '.' ? ',' : (char)currentChar;
                }
                var s = new string(new ReadOnlySpan<char>(stringBuffer, 0, count));
                value = s;
                if (options.ParseNumber) value = hadPoint ? double.Parse(s) : int.Parse(s);
                noRead = false;
            }
            else if (currentChar == 'n')
            {
                var u = reader.Read();
                var l = reader.Read();
                var ll = (currentChar = reader.Read());
                noRead = !(u == 'u' && l == 'l' && ll == 'l');
                if (!noRead) TrimNext(reader, ref currentChar);
            }
            else if (currentChar == 't')
            {
                var r = reader.Read();
                var u = reader.Read();
                var e = (currentChar = reader.Read());
                noRead = !(r == 'r' && u == 'u' && e == 'e');
                if (!noRead)
                {
                    TrimNext(reader, ref currentChar);
                    value = true;
                }
            }
            else if (currentChar == 'f')
            {
                var a = reader.Read();
                var l = reader.Read();
                var s = reader.Read();
                var e = (currentChar = reader.Read());
                noRead = !(a == 'a' && l == 'l' && s == 's' && e == 'e');
                if (!noRead)
                {
                    TrimNext(reader, ref currentChar);
                    value = false;
                }
            }

            if (!reader.EndOfStream && (currentChar < 34)) TrimNext(reader, ref currentChar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrimNext(StreamReader reader, ref int currentChar)
        {
            while (!reader.EndOfStream && (currentChar = reader.Read()) < 34) continue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetError(string errorMessage)
        {
            this.error = true;
            this.errorMessage = errorMessage;
        }

        internal struct Context
        {
            public string FieldName { get; set; }
            public bool IsInObject { get; set; }
            public bool IsInArray { get; set; }
            public int ArrayIndex { get; set; }
            public int Openings { get; set; }
            public int Closings { get; set; }
            public bool IsNull => !IsInArray && !IsInObject && Openings == 0 && Closings == 0 && FieldName == null;
        }
    }

    public class JsonMaxParserOptions
    {
        /// <summary>
        /// Defaults to False.
        /// When True, generates a flat structure of key/value. Each values are primitives value and the key is the Json Path. 
        /// Usefull fur fast and easy searching through an object hierarchy using Json Path
        /// </summary>
        public bool FlattenHierarchy { get; set; }

        /// <summary>
        /// Default to FlatArrayNotation.Indexed. 
        /// Defines the way arrays are rendered whtn the option Flat=true. 
        /// </summary>
        public FlatArrayNotation FlatArrayNotation { get; set; }

        /// <summary>
        /// When True, number fields will be directly parsed to CLR type (double for dotted numbers, int otherwise)
        /// </summary>
        public bool ParseNumber { get; set; }

        /// <summary>
        /// When True, will assume True, False or Null just based on the first char. 
        /// </summary>
        public bool OptimisticParsing { get; set; }

        /// <summary>
        /// Default to "$". 
        /// The root name to use for the document.
        /// </summary>
        public string RootName { get; set; }

        /// <summary>
        /// Defaults to 1024. 
        /// You can reduce allocations and speed by reducing this value if you know the JSON's property names and values your are deserializing contains no more that X characters.
        /// </summary>
        public int StringBufferSizeInBytes { get; set; }

    }

    public enum FlatArrayNotation
    {
        Indexed = 0,
        Dotted = 1
    }
}