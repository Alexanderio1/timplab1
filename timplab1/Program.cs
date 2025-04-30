using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace timplab1
{
    class Program
    {
        static void Main(string[] args)
        {
            VirtualArrayManager virtualArrayManager = null;
            bool isRunning = true;

            Console.WriteLine("Добро пожаловать в тестирующую программу виртуального массива!");
            Console.WriteLine("Введите \"help\" для получения списка доступных команд.");

            while (isRunning)
            {
                Console.Write("VM> ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                /* --------- распознаём команду и аргументы --------- */
                string[] commandParts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string command = commandParts[0].ToLower();
                string arguments = commandParts.Length > 1 ? commandParts[1] : null;

                switch (command)
                {
                    /* ---------- СПРАВКА ---------- */
                    case "help":
                        ShowHelp();
                        break;

                    /* ---------- CREATE ---------- */
                    // Формат: create <файл>(int | char(n) | varchar(n))
                    case "create":
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: отсутствуют аргументы. Пример:  create data.bin(int)");
                            break;
                        }

                        Match mCreate = Regex.Match(arguments,
                            @"^\s*(?<file>[^\(]+?)\s*\(\s*(?<type>.+?)\s*\)\s*$",
                            RegexOptions.IgnoreCase);

                        if (!mCreate.Success)
                        {
                            Console.WriteLine("Ошибка: неверный формат команды Create.");
                            break;
                        }

                        string fileName = mCreate.Groups["file"].Value.Trim();
                        string typeAndSize = mCreate.Groups["type"].Value.Trim().ToLower();

                        try
                        {
                            if (typeAndSize == "int")
                            {
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(int));
                                Console.WriteLine($"Создан виртуальный массив типа int в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("char"))
                            {
                                int len = int.Parse(Regex.Match(typeAndSize, @"char\s*\(\s*(\d+)\s*\)").Groups[1].Value);
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(char), len);
                                Console.WriteLine($"Создан виртуальный массив типа char({len}) в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("varchar"))
                            {
                                int len = int.Parse(Regex.Match(typeAndSize, @"varchar\s*\(\s*(\d+)\s*\)").Groups[1].Value);
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(string), len);
                                Console.WriteLine($"Создан виртуальный массив типа varchar({len}) в файле {fileName}.");
                                Console.WriteLine($"(Дополнительный файл для строк переменной длины: {fileName}.dat)");
                            }
                            else
                                Console.WriteLine("Ошибка: неизвестный тип массива.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при создании массива: {ex.Message}");
                        }
                        break;

                    /* ---------- INPUT ---------- */
                    // Формат: input (индекс, значение)
                    case "input":
                        if (virtualArrayManager == null)
                        {
                            Console.WriteLine("Ошибка: сначала выполните create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: отсутствуют аргументы. Пример:  input (0, 123)");
                            break;
                        }

                        Match mInput = Regex.Match(arguments,
                            @"^\s*\(\s*(?<idx>\d+)\s*,\s*(?<val>.+?)\s*\)\s*$");

                        if (!mInput.Success)
                        {
                            Console.WriteLine("Ошибка: неверный формат команды Input.");
                            break;
                        }

                        try
                        {
                            long index = long.Parse(mInput.Groups["idx"].Value, CultureInfo.InvariantCulture);
                            string valueStr = mInput.Groups["val"].Value.Trim();

                            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                                valueStr = valueStr.Substring(1, valueStr.Length - 2);

                            if (virtualArrayManager.ArrayType == typeof(int))
                            {
                                virtualArrayManager.WriteElement(index, int.Parse(valueStr, CultureInfo.InvariantCulture));
                                Console.WriteLine($"Записано {valueStr} в элемент {index}.");
                            }
                            else if (virtualArrayManager.ArrayType == typeof(char))
                            {
                                virtualArrayManager.WriteElement(index, valueStr);
                                Console.WriteLine($"Записана строка \"{valueStr}\" в элемент {index}.");
                            }
                            else if (virtualArrayManager.ArrayType == typeof(string))
                            {
                                virtualArrayManager.WriteElementVarchar(index, valueStr);
                                Console.WriteLine($"Записана строка (varchar) \"{valueStr}\" в элемент {index}.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при записи: {ex.Message}");
                        }
                        break;

                    /* ---------- PRINT ---------- */
                    // Формат: print (индекс)
                    case "print":
                        if (virtualArrayManager == null)
                        {
                            Console.WriteLine("Ошибка: сначала выполните create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: отсутствуют аргументы. Пример:  print (5)");
                            break;
                        }

                        Match mPrint = Regex.Match(arguments,
                            @"^\s*\(\s*(?<idx>\d+)\s*\)\s*$");

                        if (!mPrint.Success)
                        {
                            Console.WriteLine("Ошибка: неверный формат команды Print.");
                            break;
                        }

                        try
                        {
                            long index = long.Parse(mPrint.Groups["idx"].Value, CultureInfo.InvariantCulture);

                            /* — вывод значения — */
                            if (virtualArrayManager.ArrayType == typeof(int) &&
                                virtualArrayManager.ReadElement(index, out int intVal))
                                Console.WriteLine($"[{index}] = {intVal}");
                            else if (virtualArrayManager.ArrayType == typeof(char) &&
                                     virtualArrayManager.ReadElement(index, out string charVal))
                                Console.WriteLine($"[{index}] = \"{charVal}\"");
                            else if (virtualArrayManager.ArrayType == typeof(string) &&
                                     virtualArrayManager.ReadElementVarchar(index, out string varcharVal))
                                Console.WriteLine($"[{index}] = \"{varcharVal}\"");
                            else
                                Console.WriteLine($"Элемент {index} не найден (битовая карта = 0).");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при чтении: {ex.Message}");
                        }
                        break;

                    /* ---------- INFO / EXIT ---------- */
                    case "info":
                        if (virtualArrayManager != null)
                        {
                            Console.WriteLine($"Тип: {virtualArrayManager.ArrayType.Name}");
                            Console.WriteLine($"Размер: {virtualArrayManager.ArraySize} элементов");
                            Console.WriteLine($"Файл: {virtualArrayManager.FilePath}");
                            if (virtualArrayManager.ArrayType == typeof(string))
                                Console.WriteLine($"Файл строк: {virtualArrayManager.DataFilePath}");
                        }
                        else
                            Console.WriteLine("Виртуальный массив не создан.");
                        break;

                    case "exit":
                        virtualArrayManager?.Close();
                        isRunning = false;
                        Console.WriteLine("Завершение программы...");
                        break;

                    default:
                        Console.WriteLine("Неизвестная команда. Введите \"help\" для справки.");
                        break;
                }
            }
        }

        /* ---------- СПРАВКА ---------- */
        static void ShowHelp()
        {
            Console.WriteLine("\nСписок команд:");
            Console.WriteLine("  create <файл>(int | char(n) | varchar(n))   — создать массив");
            Console.WriteLine("     примеры:  create data.bin(int)");
            Console.WriteLine("               create data.bin(char(5))");
            Console.WriteLine("               create data.bin(varchar(20))\n");

            Console.WriteLine("  input (индекс, значение)                    — записать элемент");
            Console.WriteLine("     примеры:  input (0, 183)");
            Console.WriteLine("               input (15, \"Hello\")\n");

            Console.WriteLine("  print (индекс)                              — вывести элемент");
            Console.WriteLine("               print (0)\n");

            Console.WriteLine("  info                                        — информация о массиве");
            Console.WriteLine("  help                                        — эта справка");
            Console.WriteLine("  exit                                        — выход\n");
        }
    }
}
