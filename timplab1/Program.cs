﻿using System;
using System.Globalization;

namespace timplab1
{
    class Program
    {
        static void Main(string[] args)
        {
            VirtualArrayManager virtualArrayManager = null;
            bool isRunning = true;

            Console.WriteLine("Добро пожаловать в тестирующую программу виртуального массива!");
            while (isRunning)
            {
                Console.Write("VM> ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                string[] commandParts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string command = commandParts[0].ToLower();
                string arguments = commandParts.Length > 1 ? commandParts[1] : null;

                switch (command)
                {
                    case "create":
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды Create.");
                            break;
                        }
                        // Ожидаемый формат: имя_файла, тип
                        // Примеры: data.bin, int   или   data.bin, char(5)   или   data.bin, varchar(20)
                        string[] createArgs = arguments.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (createArgs.Length < 2)
                        {
                            Console.WriteLine("Ошибка: неверный формат команды Create.");
                            break;
                        }

                        string fileName = createArgs[0].Trim();
                        string typeAndSize = createArgs[1].Trim().ToLower();

                        try
                        {
                            if (typeAndSize.StartsWith("int"))
                            {
                                // Для теста размер массива задаём, например, 10000 элементов.
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(int));
                                Console.WriteLine($"Создан виртуальный массив типа int в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("char"))
                            {
                                // Ожидается формат char(длина), например char(5)
                                int start = typeAndSize.IndexOf('(');
                                int end = typeAndSize.IndexOf(')');
                                if (start < 0 || end < 0 || end <= start + 1)
                                {
                                    Console.WriteLine("Ошибка: неверный формат для типа char.");
                                    break;
                                }
                                int fixedLength = int.Parse(typeAndSize.Substring(start + 1, end - start - 1), CultureInfo.InvariantCulture);
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(char), fixedLength);
                                Console.WriteLine($"Создан виртуальный массив типа char[{fixedLength}] в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("varchar"))
                            {
                                // Ожидается формат varchar(макс. длина), например varchar(20)
                                int start = typeAndSize.IndexOf('(');
                                int end = typeAndSize.IndexOf(')');
                                if (start < 0 || end < 0 || end <= start + 1)
                                {
                                    Console.WriteLine("Ошибка: неверный формат для типа varchar.");
                                    break;
                                }
                                int maxLength = int.Parse(typeAndSize.Substring(start + 1, end - start - 1), CultureInfo.InvariantCulture);
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(string), maxLength);
                                Console.WriteLine($"Создан виртуальный массив типа varchar({maxLength}) в файле {fileName}.");
                            }
                            else
                            {
                                Console.WriteLine("Ошибка: неизвестный тип массива.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при создании массива: {ex.Message}");
                        }
                        break;

                    case "input":
                        if (virtualArrayManager == null)
                        {
                            Console.WriteLine("Ошибка: массив не создан. Используйте команду Create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды Input.");
                            break;
                        }
                        string[] inputArgs = arguments.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (inputArgs.Length < 2)
                        {
                            Console.WriteLine("Ошибка: неверный формат команды Input.");
                            break;
                        }
                        try
                        {
                            long index = long.Parse(inputArgs[0].Trim(), CultureInfo.InvariantCulture);
                            string valueStr = inputArgs[1].Trim();

                            if (virtualArrayManager.ArrayType == typeof(int))
                            {
                                int intValue = int.Parse(valueStr, CultureInfo.InvariantCulture);
                                virtualArrayManager.WriteElement(index, intValue);
                                Console.WriteLine($"Значение {intValue} записано в элемент с индексом {index}.");
                            }
                            else if (virtualArrayManager.ArrayType == typeof(char))
                            {
                                // Ожидается строка в кавычках, например "A" или "Hello" (если фиксированная длина >1)
                                // Удаляем кавычки, если есть
                                if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                                    valueStr = valueStr.Substring(1, valueStr.Length - 2);
                                virtualArrayManager.WriteElement(index, valueStr);
                                Console.WriteLine($"Строка \"{valueStr}\" записана в элемент с индексом {index}.");
                            }
                            else if (virtualArrayManager.ArrayType == typeof(string))
                            {
                                // Для varchar
                                if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                                    valueStr = valueStr.Substring(1, valueStr.Length - 2);
                                virtualArrayManager.WriteElement(index, valueStr);
                                Console.WriteLine($"Строка \"{valueStr}\" записана в элемент с индексом {index}.");
                            }
                            else
                            {
                                Console.WriteLine("Ошибка: неподдерживаемый тип массива.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при записи значения: {ex.Message}");
                        }
                        break;

                    case "print":
                        if (virtualArrayManager == null)
                        {
                            Console.WriteLine("Ошибка: массив не создан. Используйте команду Create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды Print.");
                            break;
                        }
                        try
                        {
                            long index = long.Parse(arguments.Trim(), CultureInfo.InvariantCulture);
                            if (virtualArrayManager.ArrayType == typeof(int))
                            {
                                if (virtualArrayManager.ReadElement(index, out int intValue))
                                {
                                    Console.WriteLine($"Значение элемента с индексом {index}: {intValue}");
                                }
                                else
                                {
                                    Console.WriteLine($"Ошибка: элемент с индексом {index} не найден.");
                                }
                            }
                            else if (virtualArrayManager.ArrayType == typeof(char) || virtualArrayManager.ArrayType == typeof(string))
                            {
                                if (virtualArrayManager.ReadElement(index, out string strValue))
                                {
                                    Console.WriteLine($"Значение элемента с индексом {index}: \"{strValue}\"");
                                }
                                else
                                {
                                    Console.WriteLine($"Ошибка: элемент с индексом {index} не найден.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при чтении значения: {ex.Message}");
                        }
                        break;

                    case "exit":
                        Console.WriteLine("Завершение программы...");
                        // Здесь можно добавить закрытие файлов и выгрузку буфера
                        virtualArrayManager?.Close();
                        isRunning = false;
                        break;

                    default:
                        Console.WriteLine("Ошибка: неизвестная команда.");
                        break;
                }
            }
        }
    }
}
