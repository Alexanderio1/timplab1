using System;
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
            Console.WriteLine("Введите 'help' для получения списка доступных команд.");

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
                    case "help":
                        ShowHelp();
                        break;

                    case "create":
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды Create.");
                            break;
                        }
                        // Формат: имя_файла, тип
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
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(int));
                                Console.WriteLine($"Создан виртуальный массив типа int в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("char"))
                            {
                                int start = typeAndSize.IndexOf('(');
                                int end = typeAndSize.IndexOf(')');
                                if (start < 0 || end < 0 || end <= start + 1)
                                {
                                    Console.WriteLine("Ошибка: неверный формат для типа char.");
                                    break;
                                }
                                int fixedLength = int.Parse(typeAndSize.Substring(start + 1, end - start - 1), CultureInfo.InvariantCulture);
                                virtualArrayManager = new VirtualArrayManager(fileName, 10000, typeof(char), fixedLength);
                                Console.WriteLine($"Создан виртуальный массив типа char({fixedLength}) в файле {fileName}.");
                            }
                            else if (typeAndSize.StartsWith("varchar"))
                            {
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
                                Console.WriteLine($"(Дополнительный файл для строк переменной длины: {fileName}.dat)");
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
                            Console.WriteLine("Ошибка: массив не создан. Используйте команду create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды input.");
                            break;
                        }
                        {
                            // Разбиваем аргументы по запятой: <индекс>, <значение>
                            string[] inputArgs = arguments.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
                            if (inputArgs.Length < 2)
                            {
                                Console.WriteLine("Ошибка: неверный формат команды input.");
                                break;
                            }
                            try
                            {
                                long index = long.Parse(inputArgs[0].Trim(), CultureInfo.InvariantCulture);
                                string valueStr = inputArgs[1].Trim();

                                // Если строка обрамлена кавычками, снимаем их
                                if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                                    valueStr = valueStr.Substring(1, valueStr.Length - 2);

                                if (virtualArrayManager.ArrayType == typeof(int))
                                {
                                    int intValue = int.Parse(valueStr, CultureInfo.InvariantCulture);
                                    virtualArrayManager.WriteElement(index, intValue);
                                    Console.WriteLine($"Значение {intValue} записано в элемент с индексом {index}.");
                                }
                                else if (virtualArrayManager.ArrayType == typeof(char))
                                {
                                    virtualArrayManager.WriteElement(index, valueStr);
                                    Console.WriteLine($"Строка \"{valueStr}\" записана в элемент с индексом {index}.");
                                }
                                else if (virtualArrayManager.ArrayType == typeof(string))
                                {
                                    // Для varchar вызываем специальный метод
                                    virtualArrayManager.WriteElementVarchar(index, valueStr);
                                    Console.WriteLine($"Строка (varchar) \"{valueStr}\" записана в элемент с индексом {index}.");
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
                        }
                        break;

                    case "print":
                        if (virtualArrayManager == null)
                        {
                            Console.WriteLine("Ошибка: массив не создан. Используйте команду create.");
                            break;
                        }
                        if (arguments == null)
                        {
                            Console.WriteLine("Ошибка: недостаточно аргументов для команды print.");
                            break;
                        }
                        {
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
                                        Console.WriteLine($"Ошибка: элемент с индексом {index} не найден (битовая карта = 0).");
                                    }
                                }
                                else if (virtualArrayManager.ArrayType == typeof(char))
                                {
                                    if (virtualArrayManager.ReadElement(index, out string strValue))
                                    {
                                        Console.WriteLine($"Значение элемента с индексом {index}: \"{strValue}\"");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Ошибка: элемент с индексом {index} не найден (битовая карта = 0).");
                                    }
                                }
                                else if (virtualArrayManager.ArrayType == typeof(string))
                                {
                                    if (virtualArrayManager.ReadElementVarchar(index, out string varStr))
                                    {
                                        Console.WriteLine($"Значение элемента (varchar) с индексом {index}: \"{varStr}\"");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Ошибка: элемент с индексом {index} не найден (битовая карта = 0).");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка при чтении значения: {ex.Message}");
                            }
                        }
                        break;

                    case "info":
                        if (virtualArrayManager != null)
                        {
                            Console.WriteLine("Информация о виртуальном массиве:");
                            Console.WriteLine($"Тип: {virtualArrayManager.ArrayType.Name}");
                            Console.WriteLine($"Размер массива: {virtualArrayManager.ArraySize} элементов");
                            Console.WriteLine($"Файл: {virtualArrayManager.FilePath}");
                            if (virtualArrayManager.ArrayType == typeof(string))
                            {
                                Console.WriteLine($"Файл строк (для varchar): {virtualArrayManager.DataFilePath}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Виртуальный массив не создан. Используйте команду create.");
                        }
                        break;

                    case "exit":
                        Console.WriteLine("Завершение программы...");
                        virtualArrayManager?.Close();
                        isRunning = false;
                        break;

                    default:
                        Console.WriteLine("Ошибка: неизвестная команда. Введите 'help' для справки.");
                        break;
                }
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("\nСписок доступных команд:");
            Console.WriteLine("  create <имя_файла>, <тип>  - Создать виртуальный массив.");
            Console.WriteLine("       Примеры:");
            Console.WriteLine("         create data.bin, int");
            Console.WriteLine("         create data.bin, char(5)");
            Console.WriteLine("         create data.bin, varchar(20)");
            Console.WriteLine("  input <индекс>, <значение> - Записать значение в элемент массива.");
            Console.WriteLine("       Примеры:");
            Console.WriteLine("         input 0, 183        (для int)");
            Console.WriteLine("         input 15, \"Hello\"  (для char / varchar)");
            Console.WriteLine("  print <индекс>             - Вывести значение элемента массива по индексу.");
            Console.WriteLine("         print 0");
            Console.WriteLine("  info                       - Вывести информацию о созданном виртуальном массиве.");
            Console.WriteLine("  help                       - Показать справочную информацию.");
            Console.WriteLine("  exit                       - Завершить программу.\n");
        }
    }
}
