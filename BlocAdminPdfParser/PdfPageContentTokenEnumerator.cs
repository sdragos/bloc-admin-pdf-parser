using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace BlocAdminPdfParser
{
    public class PdfPageContentTokenEnumerator : IEnumerator<String>, IDisposable
    {
        private readonly string _pageContent;
        private StringReader _reader = null;

        private String[] _currentLineTokens;
        private Int32 _currentLineIndex = 0;

        private LinkedList<String> _seenTokens;

        public PdfPageContentTokenEnumerator(String pageContent)
        {
            _pageContent = pageContent;
            Reset();
        }

        public bool MoveNext()
        {
            if (_currentLineTokens == null || _currentLineIndex == _currentLineTokens.Length - 1)
            {
                String line = null;
                if ((line = _reader.ReadLine()) == null)
                {
                    return false;
                }

                _currentLineIndex = 0;
                if (line.Length > 4 && line.StartsWith('(') && line.EndsWith(")Tj"))
                {
                    line = line.Insert(line.Length - 2, " ");
                }

                _currentLineTokens = line.Trim().Split(' ');
                SetCurrentToken(_currentLineTokens[_currentLineIndex]);

                return true;
            }

            _currentLineIndex++;
            SetCurrentToken(_currentLineTokens[_currentLineIndex]);

            return true;
        }

        public void Reset()
        {
            _reader?.Dispose();
            _reader = new StringReader(_pageContent);
            _seenTokens = new LinkedList<string>();
        }

        public void ClearTokenList()
        {
            CurrentTokenListNode?.List.Clear();
        }

        public LinkedListNode<String> CurrentTokenListNode { get; private set; }

        public string Current { get; private set; }

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            _reader?.Dispose();
        }

        private void SetCurrentToken(String token)
        {
            _seenTokens.AddLast(token);
            Current = token;
            CurrentTokenListNode = _seenTokens.Last;
        }
    }
}
