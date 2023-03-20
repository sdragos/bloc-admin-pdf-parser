using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.XPath;
using iTextSharp.text.pdf;
using Org.BouncyCastle.Bcpg;

namespace BlocAdminPdfParser
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1){
                Console.WriteLine("Usage: dotnet BlocAdminPdfParser.dll path-to-file-1 [path-to-file-2] ..[path-to-file-n]");
            }

            foreach (String arg in args){
                if (String.IsNullOrWhiteSpace(arg))
                    continue;

                TryProcessPdf(arg);
            }
        }

        private static Boolean TryProcessPdf(String filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("The specified file was not found.");

                String fileNamePrefix = Path.GetFileNameWithoutExtension(filePath);

                PdfReader pdfReader = new PdfReader(filePath);

                for (int i = 1; i <= pdfReader.NumberOfPages; i++)
                {
                    try
                    {
                        var pageContentByteArray = pdfReader.GetPageContent(i);
                        String pageContent = Encoding.UTF8.GetString(pageContentByteArray);

                        var textByCoords = new Dictionary<decimal, Dictionary<Decimal, (decimal x, decimal y, string text)>>();
                        var drawPointsByXCoordinate = new Dictionary<decimal, List<decimal>>();

                        var linesForEachXCoord = new Dictionary<decimal, HashSet<(decimal y1, decimal y2)>>();
                        var linesForEachYCoord = new Dictionary<decimal, HashSet<(decimal x1, decimal x2)>>();

                        GetTextByCoordinatesAndDrawPointsByCoordinateX(
                            pageContent, 
                            textByCoords,
                            drawPointsByXCoordinate,
                            linesForEachXCoord,
                            linesForEachYCoord);

                        // foreach (var xCoord in linesForEachXCoord.Keys)
                        // {
                        //     var minimizedList = new List<(decimal y1, decimal y2)>();
                        //     
                        //     var ordered = linesForEachXCoord[xCoord].OrderBy(item => item.y1 * 1000000 + item.y2).ToArray();
                        //     
                        //     for (int k = 0; k < ordered.Length; k++)
                        //     {
                        //         if (minimizedList.Count == 0)
                        //         {
                        //             minimizedList.Add((ordered[k].y1, ordered[k].y2));
                        //             continue;
                        //         }
                        //         else
                        //         {
                        //             Boolean minimized = false;
                        //             for (int z = 0 ; z < minimizedList.Count; z++)
                        //             {
                        //                 var minimizedEntry = minimizedList[z];
                        //                 if (ordered[k].y1 >= minimizedEntry.y1 && ordered[k].y2 <= minimizedEntry.y2)
                        //                 {
                        //                     //contained
                        //                     minimized = true;
                        //                     break;
                        //                 }
                        //
                        //                 if (ordered[k].y1 >= minimizedEntry.y1 && ordered[k].y1 <= minimizedEntry.y2 &&
                        //                     ordered[k].y2 > minimizedEntry.y2)
                        //                 {
                        //                     //extension
                        //                     minimizedEntry.y2 = ordered[k].y2;
                        //                     minimizedList[z] = minimizedEntry;
                        //                     minimized = true;
                        //                     break;
                        //                 }
                        //
                        //                 minimizedList.Add((ordered[k].y1, ordered[k].y2));
                        //             }
                        //         }
                        //     }
                        //
                        //     linesForEachXCoord[xCoord].Clear();
                        //     foreach (var item in minimizedList)
                        //         linesForEachXCoord[xCoord].Add(item);
                        // }

                        if (SameCountOfYCoordinatesForAllEntries(drawPointsByXCoordinate))
                        {
                            //we have a table, let's process data to extract the table content
                            var sortedXCoords = drawPointsByXCoordinate.Keys.ToList();
                            sortedXCoords.Sort();
                            var sortedYCoords = drawPointsByXCoordinate.First().Value.ToList();
                            sortedYCoords.Sort();

                            List<List<String>> rows = new List<List<string>>();
                            for (int yCoordIndex = sortedYCoords.Count - 2; yCoordIndex >= 0; yCoordIndex--)
                            {
                                var columnsOfCurrentRow = new List<string>();
                                rows.Add(columnsOfCurrentRow);

                                for (int xCoordIndex = 1; xCoordIndex <= sortedXCoords.Count; xCoordIndex++)
                                {
                                    StringBuilder cell = new StringBuilder();

                                    var cellContentCandidates = textByCoords
                                        .Where(t => t.Key > sortedXCoords[xCoordIndex - 1] &&
                                                    t.Key < sortedXCoords[xCoordIndex])
                                        .Select(t => t.Key).ToList();
                                    cellContentCandidates.Sort();

                                    for (int j = cellContentCandidates.Count - 1; j >= 0; j--)
                                    {
                                        var cellContentValues = textByCoords[cellContentCandidates[j]]
                                            .Where(t => t.Key > sortedYCoords[yCoordIndex] &&
                                                        t.Key < sortedYCoords[yCoordIndex + 1])
                                            .Select(t => t.Value)
                                            .OrderByDescending(t => t.y)
                                            .ToList();

                                        cell.Append(
                                            $" {String.Join(' ', cellContentValues.Select(c => c.text.Trim()))}");
                                    }

                                    columnsOfCurrentRow.Add(cell.ToString().Trim());
                                }
                            }

                            decimal lastY = sortedYCoords.Last();
                            decimal firstY = sortedYCoords.First();

                            var nonTableItemsByCoordinateY = textByCoords
                                .SelectMany(t => t.Value.Where(u => u.Key > lastY || u.Key < firstY))
                                .ToList()
                                .GroupBy(sel => sel.Value.y)
                                .OrderByDescending(sel => sel.Key);

                            // assuming that when there are only 2 items at the same Y coordinate, they represent a pair of values
                            var otherKeyValueData = new Dictionary<string, string>();
                            foreach (var nonTableItemsAtCoordinateY in nonTableItemsByCoordinateY)
                            {
                                var itemsAtCoordinateY = nonTableItemsAtCoordinateY.OrderBy(kv => kv.Value.x).ToList();
                                if (itemsAtCoordinateY.Count == 1)
                                {
                                    otherKeyValueData[itemsAtCoordinateY[0].Value.text.Trim()] = "";
                                }
                                else
                                {
                                    if (itemsAtCoordinateY.Count == 2)
                                    {
                                        otherKeyValueData[itemsAtCoordinateY[0].Value.text.Trim()] =
                                            itemsAtCoordinateY[1].Value.text.Trim();
                                    }
                                    else
                                    {
                                        // what do you we do here?
                                    }
                                }
                            }

                            File.WriteAllText($"{fileNamePrefix}-{i}-pdf-dump.txt", pageContent);
                            File.WriteAllLines($"{fileNamePrefix}-{i}-table-w-header.txt", rows.Select(x =>
                                string.Join(",", x.Select(str =>
                                {
                                    if (str.Contains(',') || str.Contains('"'))
                                    {
                                        return $"\"{str.Replace("\"", "\"\"")}\"";
                                    }

                                    return str;
                                }))));

                            File.WriteAllLines($"{fileNamePrefix}-{i}-info-w-header.txt",
                                otherKeyValueData.Select(kv => $"{kv.Key}{kv.Value}"));
                        }
                        else
                        {
                            Console.WriteLine($"Cannot identify table on page {i}");
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Error processing page {i}");
                    }
                }
            }
            catch(Exception e){
                Console.WriteLine($"Error processing {filePath}. Error: {e.Message}");
                return false;
            }
            Console.WriteLine($"Finished processing {filePath}");
            return true;
        }

        private static Boolean SameCountOfYCoordinatesForAllEntries(Dictionary<decimal, List<decimal>> drawPointsByCoord)
        {
            if (drawPointsByCoord.Keys.Count <= 1)
                return false;

            var firstCols = new HashSet<decimal>(drawPointsByCoord.First().Value);

            foreach (var value in drawPointsByCoord.Values)
            {
                if (value.Count != firstCols.Count)
                    return false;

                if (!new HashSet<decimal>(value).SetEquals(firstCols))
                    return false;
            }

            return true;
        }

        private static void GetTextByCoordinatesAndDrawPointsByCoordinateX(
            string pageContent,
            Dictionary<decimal, Dictionary<decimal, (decimal x, decimal y, string text)>> textByCoords,
            Dictionary<decimal, List<decimal>> drawPointsByXCoord,
            Dictionary<decimal, HashSet<(decimal y1, decimal y2)>> linesForEachXCoord,
            Dictionary<decimal, HashSet<(decimal x1, decimal x2)>> linesForEachYCoord)
        {
            using (var pdfPageContentTokenEnumerator = new PdfPageContentTokenEnumerator(pageContent))
            {
                var yUsedValues = new HashSet<Decimal>();
                var alreadyAddedPoints = new HashSet<(decimal x, decimal y)>();

                (Decimal a, Decimal b, Decimal c, Decimal d, Decimal e, Decimal f) tm = (1, 0, 0, 1, 0, 0);
                var transformationMatrixStack = new Stack<(Decimal a, Decimal b, Decimal c, Decimal d, Decimal e, Decimal f)>();

                void AddCoordinates(Decimal x, Decimal y)
                {
                    //adjust coordinates using current transformation matrix
                    x = tm.a * x + tm.c * y + tm.e;
                    y = tm.b * x + tm.d * y + tm.f;

                    if ((x >= 458.36m && x <= 458.46m) || (x >= 428.025m && x <= 428.035m) || (x >= 470.545m && x <= 470.555m) || (x >= 543.445m && x <= 543.455m))
                        return;//artefacts and stupid code web section

                    Decimal xApprox = drawPointsByXCoord.Keys.FirstOrDefault(k => Math.Abs(x - k) <= 0.03m);
                    x = xApprox == default ? x : xApprox;

                    Decimal yApprox = yUsedValues.FirstOrDefault(k => Math.Abs(y - k) <= 0.03m);
                    y = yApprox == default ? y : yApprox;

                    if (!alreadyAddedPoints.Contains((x, y)))
                    {
                        List<decimal> yCoords;
                        if (!drawPointsByXCoord.TryGetValue(x, out yCoords))
                        {
                            yCoords = new List<decimal>();
                            drawPointsByXCoord.Add(x, yCoords);
                        }

                        yUsedValues.Add(y);
                        yCoords.Add(y);
                        alreadyAddedPoints.Add((x, y));
                    }
                }
                
                void AddLine(Decimal x1, Decimal y1, Decimal x2, Decimal y2)
                {
                    if (x1 != x2 && y1 != y2)
                        return; //skip this line

                    if (x1 == x2)
                    {
                        HashSet<(decimal y1, decimal y2)> coordList;
                        if (!linesForEachXCoord.TryGetValue(x1, out coordList))
                        {
                            coordList = new HashSet<(decimal y1, decimal y2)>();
                            linesForEachXCoord.Add(x1, coordList);
                        }
                        coordList.Add((Math.Min(y1, y2), Math.Max(y1, y2)));

                        // Decimal max = Math.Max(y1, y2);
                        // Decimal min = Math.Min(y1, y2);
                        // if (linesForEachXCoord.TryGetValue(x1, out var lineYCoords))
                        // {
                        //     if (lineYCoords.y1 > max || lineYCoords.y2 < min) 
                        //         throw new InvalidOperationException();
                        //
                        //     if (lineYCoords.y1 <= min && lineYCoords.y2 >= max) 
                        //         return; //contained
                        //
                        //     if (lineYCoords.y1 <= min && lineYCoords.y2 >= min) 
                        //         lineYCoords.y2 = max; //right extension
                        //
                        //     if (lineYCoords.y1 < min && lineYCoords.y2 >= min && lineYCoords.y2 < max)
                        //         lineYCoords.y1 = min;
                        // }
                        // else
                        // {
                        //     linesForEachXCoord.Add(x1, (min, max));
                        // }
                    }

                    if (y1 == y2)
                    {
                        HashSet<(decimal x1, decimal x2)> coordList;
                        if (!linesForEachYCoord.TryGetValue(y1, out coordList))
                        {
                            coordList = new HashSet<(decimal x1, decimal x2)>();
                            linesForEachYCoord.Add(y1, coordList);
                        }
                        coordList.Add((Math.Min(x1, x2), Math.Max(x1, x2)));

                        // Decimal max = Math.Max(x1, x2);
                        // Decimal min = Math.Min(x1, x2);
                        // if (linesForEachYCoord.TryGetValue(y1, out var lineXCoords))
                        // {
                        //     if (lineXCoords.x1 > max || lineXCoords.x2 < min)
                        //         throw new InvalidOperationException();
                        //
                        //     if (lineXCoords.x1 <= min && lineXCoords.x2 >= max)
                        //         return; //contained
                        //
                        //     if (lineXCoords.x1 <= min && lineXCoords.x2 >= min)
                        //         lineXCoords.x2 = max; //right extension
                        //
                        //     if (lineXCoords.x1 < min && lineXCoords.x2 >= min && lineXCoords.x2 < max)
                        //         lineXCoords.x1 = min;
                        // }
                        // else
                        // {
                        //     linesForEachYCoord.Add(y1, (min, max));
                        // }
                    }
                }

                LinkedListNode<String> workNode = null;

                Boolean insideTextEntry = false;
                Decimal textPosX = 0;
                Decimal textPosY = 0;
                String text;

                Decimal lineOriginX = 0;
                Decimal lineDestinationX = 0;
                Decimal lineOriginY = 0;
                Decimal lineDestinationY = 0;

                while (pdfPageContentTokenEnumerator.MoveNext())
                {
                    switch (pdfPageContentTokenEnumerator.Current)
                    {
                        case PdfOperator.q: //save
                            transformationMatrixStack.Push(tm);
                            break;
                        case PdfOperator.Q: //restore
                            tm = transformationMatrixStack.Pop();
                            break;
                        case PdfOperator.cm: //set coordinate transformation matrix
                            workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.f = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.e = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.d = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.c = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.b = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            tm.a = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            break;
                        case PdfOperator.Tf: //set font
                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        case PdfOperator.Tj: //set text
                            if (!insideTextEntry)
                                throw new InvalidOperationException();

                            workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;
                            workNode = workNode.PreviousNotNullOrThrow();

                            pdfPageContentTokenEnumerator.CurrentTokenListNode.List.RemoveLast();
                            text = String.Join(' ', workNode.List);
                            text = text.Substring(1, text.Length - 2);

                            if (!textByCoords.TryGetValue(textPosX, out var byY))
                            {
                                byY = new Dictionary<decimal, (decimal x, decimal y, string text)>();
                                textByCoords[textPosX] = byY;
                            }

                            var entry = (textPosX, textPosY, text);

                            if (byY.TryGetValue(textPosY, out var existingEntry))
                            {
                                if (entry != existingEntry)
                                {
                                    //sometimes it may happen that 2 entries are added at the same place, one for text 
                                    //rendering compatibility purposes and another one with codes, keep the shorter version
                                    if (entry.text.Length < existingEntry.text.Length)
                                    {
                                        byY[textPosY] = entry;
                                    }
                                }
                            }
                            else
                            {
                                byY.Add(textPosY, entry);
                            }

                            break;

                        case PdfOperator.Tm: //text transformation (only translation supported by us, equivalent to Td)
                            if (!insideTextEntry)
                                throw new InvalidOperationException();

                            workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;
                            workNode = workNode.PreviousNotNullOrThrow();
                            textPosY = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousNotNullOrThrow();
                            textPosX = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                            workNode = workNode.PreviousValueEqualToOrThrow("1");
                            workNode = workNode.PreviousValueEqualToOrThrow("0");
                            workNode = workNode.PreviousValueEqualToOrThrow("0");
                            workNode.PreviousValueEqualToOrThrow("1");

                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        case PdfOperator.Td: //set text position
                            if (!insideTextEntry)
                                throw new InvalidOperationException();

                            workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;
                            workNode = workNode.PreviousNotNullOrThrow();

                            textPosY += Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                            workNode = workNode.PreviousNotNullOrThrow();

                            textPosX += Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        case PdfOperator.BT: //begin text
                            insideTextEntry = true;
                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        case PdfOperator.ET: //end text
                            insideTextEntry = false;
                            pdfPageContentTokenEnumerator.ClearTokenList();
                            textPosY = 0;
                            textPosX = 0;
                            break;

                        case PdfOperator.re:  //draw rectangle
                            if (!insideTextEntry)
                            {
                                workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;
                                workNode = workNode.PreviousNotNullOrThrow();
                                Decimal rectangleHeight = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                                workNode = workNode.PreviousNotNullOrThrow();
                                Decimal rectangleWidth = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                workNode = workNode.PreviousNotNullOrThrow();
                                lineOriginY = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);
                                workNode = workNode.PreviousNotNullOrThrow();
                                lineOriginX = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                AddCoordinates(lineOriginX, lineOriginY);
                                AddCoordinates(lineOriginX+rectangleWidth, lineOriginY);
                                AddCoordinates(lineOriginX, lineOriginY+rectangleHeight);
                                AddCoordinates(lineOriginX+rectangleWidth, lineOriginY+rectangleHeight);

                                // AddLine(lineOriginX, lineOriginY, lineOriginX+rectangleWidth, lineOriginY);
                                // AddLine(lineOriginX, lineOriginY, lineOriginX, lineOriginY+rectangleHeight);
                                // AddLine(lineOriginX+rectangleWidth, lineOriginY, lineOriginX+rectangleWidth, lineOriginY+rectangleHeight);
                                // AddLine(lineOriginX, lineOriginY+rectangleHeight, lineOriginX+rectangleWidth, lineOriginY+rectangleHeight);
                            }
                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        case PdfOperator.S: //draw
                            if (!insideTextEntry)
                            {
                                workNode = pdfPageContentTokenEnumerator.CurrentTokenListNode;

                                if (workNode.Previous == null || workNode.PreviousValueEqualTo("re"))
                                {
                                    pdfPageContentTokenEnumerator.ClearTokenList();
                                    break;
                                }

                                workNode = workNode.PreviousValueEqualToOrThrow("l");

                                workNode = workNode.PreviousNotNullOrThrow();

                                lineOriginY = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                workNode = workNode.PreviousNotNullOrThrow();

                                lineOriginX = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                AddCoordinates(lineOriginX, lineOriginY);

                                workNode = workNode.PreviousValueEqualToOrThrow("m");

                                workNode = workNode.PreviousNotNullOrThrow();

                                lineDestinationY = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                workNode = workNode.PreviousNotNullOrThrow();

                                lineDestinationX = Decimal.Parse(workNode.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

                                AddCoordinates(lineDestinationX, lineDestinationY);

                                //AddLine(lineOriginX, lineOriginY, lineDestinationX, lineDestinationY);
                            }

                            pdfPageContentTokenEnumerator.ClearTokenList();
                            break;

                        default:
                            break;
                    }
                }
            }
        }
    }
}
