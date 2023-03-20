using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace BlocAdminPdfParser
{
    public static class LinkedListExtensions
    {
        public static Boolean PreviousValueEqualTo(this LinkedListNode<String> currentNode, String value)
        {
            if (currentNode.Previous?.Value == value)
                return true;
            return false;
        }

        public static LinkedListNode<String> PreviousValueEqualToOrThrow(this LinkedListNode<String> currentNode, String value)
        {
            if (currentNode == null)
            {
                throw new ArgumentNullException(nameof(currentNode));
            }

            var node = currentNode.Previous;
            if (node?.Value != value)
            {
                throw new InvalidOperationException();
            }

            return node;
        }

        public static LinkedListNode<String> PreviousNotNullOrThrow(this LinkedListNode<String> currentNode)
        {
            if (currentNode == null)
            {
                throw new ArgumentNullException(nameof(currentNode));
            }

            var node = currentNode.Previous;
            if (node == null)
            {
                throw new InvalidOperationException();
            }

            return node;
        }
    }
}
