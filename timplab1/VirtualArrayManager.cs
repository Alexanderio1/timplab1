using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace timplab1
{
    public class VirtualArrayManager
    {
        private const int DEFAULT_PAGE_SIZE = 512;    // базовый размер блока для int
        private const int SIGNATURE_LENGTH = 2;         // Сигнатура – 2 байта ("VM")
        public string FilePath { get; private set; }

        public long ArraySize { get; private set; }     // Общее количество элементов
        public Type ArrayType { get; private set; }       // Тип элементов (int, char, string)
        public int FixedStringLength { get; private set; } // Для char – длина строки; для varchar – макс. длина

        private int ElementsPerPage;                    // Количество элементов на страницу
        private int TotalPages;                         // Общее число страниц
        private FileStream fileStream;                  // Файловый поток для работы с файлом подкачки

        // Буфер страниц – хранит загруженные страницы
        private List<Page> PageBuffer;

        // Для int страница всегда DEFAULT_PAGE_SIZE, для строковых типов вычисляем выровненный размер
        public int PageSize { get; private set; }

        // Дополнительный файловый поток для строк (используется только для varchar)
        private FileStream dataFileStream;
        private string dataFilePath;

        public VirtualArrayManager(string filePath, long arraySize, Type arrayType, int fixedStringLength = 0)
        {
            FilePath = filePath;
            ArraySize = arraySize;
            ArrayType = arrayType;
            FixedStringLength = fixedStringLength;
            PageBuffer = new List<Page>();

            // Определяем ElementsPerPage и PageSize в зависимости от типа
            if (ArrayType == typeof(int))
            {
                // Для int: 124 элемента на страницу, 124*4 + 16 = 512 байт.
                ElementsPerPage = 124;
                PageSize = DEFAULT_PAGE_SIZE;
            }
            else if (ArrayType == typeof(char))
            {
                // Для char: по заданию 128 элементов
                ElementsPerPage = 128;
                PageSize = ComputePageSizeForStrings(FixedStringLength);
            }
            else if (ArrayType == typeof(string))
            {
                // Для varchar: на странице хранятся 128 адресов (int) – данные во внешнем файле.
                ElementsPerPage = 128;
                PageSize = ComputePageSizeForStrings(4); // для swap-страницы используем 4 байта на элемент
                // Инициализируем файл для хранения строк (адресуемый через swap-страницу)
                dataFilePath = Path.ChangeExtension(FilePath, ".dat");
                dataFileStream = new FileStream(dataFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            else
            {
                throw new ArgumentException("Неподдерживаемый тип массива.");
            }

            // Вычисляем общее число страниц (округляем вверх)
            TotalPages = (int)Math.Ceiling(ArraySize / (double)ElementsPerPage);

            // Открываем или создаем файл подкачки
            InitializeFile();

            // Загружаем начальный буфер страниц (минимум 3 страницы)
            LoadInitialPages();
        }

        /// <summary>
        /// Вычисляет размер страницы для строковых типов (char или varchar),
        /// выравнивая сырой размер до ближайшего кратного 512.
        /// bytesPerElement – количество байт, отводимое под элемент на swap-странице:
        /// для char это FixedStringLength, для varchar – 4 (адрес).
        /// </summary>
        private int ComputePageSizeForStrings(int bytesPerElement)
        {
            int bitmapLength = (int)Math.Ceiling(ElementsPerPage / 8.0);
            int rawSize = ElementsPerPage * bytesPerElement + bitmapLength;
            int pageSize = 512 * (int)Math.Ceiling(rawSize / 512.0);
            return pageSize;
        }

        private void InitializeFile()
        {
            bool fileExists = File.Exists(FilePath);
            fileStream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (!fileExists)
            {
                // Записываем сигнатуру ("VM")
                byte[] signature = System.Text.Encoding.ASCII.GetBytes("VM");
                fileStream.Write(signature, 0, SIGNATURE_LENGTH);

                // Резервируем место под страницы: TotalPages * PageSize байт
                fileStream.SetLength(SIGNATURE_LENGTH + TotalPages * PageSize);
            }
        }

        private void LoadInitialPages()
        {
            int pagesToLoad = Math.Min(3, TotalPages);
            for (int i = 0; i < pagesToLoad; i++)
            {
                Page page = LoadPageFromFile(i);
                PageBuffer.Add(page);
            }
        }

        // Получение страницы из буфера или её загрузка
        private Page GetPage(long elementIndex)
        {
            int absolutePageNumber = (int)(elementIndex / ElementsPerPage);
            Page page = PageBuffer.Find(p => p.PageNumber == absolutePageNumber);
            if (page != null)
            {
                page.LastAccessTime = DateTime.Now;
                return page;
            }
            if (PageBuffer.Count >= 3)
            {
                Page oldestPage = PageBuffer[0];
                foreach (var p in PageBuffer)
                {
                    if (p.LastAccessTime < oldestPage.LastAccessTime)
                        oldestPage = p;
                }
                if (oldestPage.ModificationFlag)
                    SavePageToFile(oldestPage);
                PageBuffer.Remove(oldestPage);
            }
            Page newPage = LoadPageFromFile(absolutePageNumber);
            PageBuffer.Add(newPage);
            return newPage;
        }

        // Сохранение страницы в файл
        private void SavePageToFile(Page page)
        {
            Console.WriteLine($"Сохраняем страницу {page.PageNumber} в файл.");
            long pagePosition = SIGNATURE_LENGTH + page.PageNumber * PageSize;
            fileStream.Seek(pagePosition, SeekOrigin.Begin);
            byte[] pageBytes = page.ToByteArray(ElementsPerPage, ArrayType, FixedStringLength, PageSize);
            fileStream.Write(pageBytes, 0, pageBytes.Length);
            fileStream.Flush();
            page.ModificationFlag = false;
        }

        // Загрузка страницы из файла
        private Page LoadPageFromFile(int pageNumber)
        {
            Console.WriteLine($"Загружаем страницу {pageNumber} из файла.");
            long pagePosition = SIGNATURE_LENGTH + pageNumber * PageSize;
            fileStream.Seek(pagePosition, SeekOrigin.Begin);
            byte[] pageBytes = new byte[PageSize];
            fileStream.Read(pageBytes, 0, PageSize);
            Page page = Page.FromByteArray(pageBytes, pageNumber, ElementsPerPage, ArrayType, FixedStringLength, PageSize);
            page.LastAccessTime = DateTime.Now;
            return page;
        }

        // Чтение для int
        public bool ReadElement(long elementIndex, out int value)
        {
            value = 0;
            if (ArrayType != typeof(int))
                throw new InvalidOperationException("Неверный тип операции чтения для данного массива.");
            Page page = GetPage(elementIndex);
            int offset = (int)(elementIndex % ElementsPerPage);
            if (!page.IsElementSet(offset))
            {
                Console.WriteLine("Элемент не был записан (битовая карта = 0).");
                return false;
            }
            value = page.GetIntValue(offset);
            return true;
        }

        // Запись для int
        public bool WriteElement(long elementIndex, int value)
        {
            if (ArrayType != typeof(int))
                throw new InvalidOperationException("Неверный тип операции записи для данного массива.");
            Page page = GetPage(elementIndex);
            int offset = (int)(elementIndex % ElementsPerPage);
            page.SetIntValue(offset, value);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        // Чтение для строк (char или varchar)
        public bool ReadElement(long elementIndex, out string value)
        {
            value = "";
            if (ArrayType == typeof(char))
            {
                Page page = GetPage(elementIndex);
                int offset = (int)(elementIndex % ElementsPerPage);
                if (!page.IsElementSet(offset))
                {
                    Console.WriteLine("Элемент не был записан (битовая карта = 0).");
                    return false;
                }
                value = page.GetStringValue(offset, FixedStringLength);
                return true;
            }
            else if (ArrayType == typeof(string))
            {
                return ReadVarcharElement(elementIndex, out value);
            }
            else
            {
                throw new InvalidOperationException("Неверный тип операции чтения для данного массива.");
            }
        }

        // Запись для строк (char или varchar)
        public bool WriteElement(long elementIndex, string value)
        {
            if (ArrayType == typeof(char))
            {
                Page page = GetPage(elementIndex);
                int offset = (int)(elementIndex % ElementsPerPage);
                page.SetStringValue(offset, value, FixedStringLength);
                page.ModificationFlag = true;
                page.LastAccessTime = DateTime.Now;
                return true;
            }
            else if (ArrayType == typeof(string))
            {
                return WriteVarcharElement(elementIndex, value);
            }
            else
            {
                throw new InvalidOperationException("Неверный тип операции записи для данного массива.");
            }
        }

        // Запись для varchar: сохраняет строку в дополнительном файле и записывает её адрес в swap-файл.
        private bool WriteVarcharElement(long elementIndex, string value)
        {
            // Определяем смещение для записи в dataFileStream
            long offset = dataFileStream.Length;
            byte[] valueBytes = System.Text.Encoding.ASCII.GetBytes(value);
            int len = valueBytes.Length;
            byte[] lenBytes = BitConverter.GetBytes(len);
            dataFileStream.Seek(0, SeekOrigin.End);
            dataFileStream.Write(lenBytes, 0, 4);
            dataFileStream.Write(valueBytes, 0, len);
            dataFileStream.Flush();

            if (offset > int.MaxValue)
                throw new Exception("Файл данных слишком велик.");
            int address = (int)offset;

            // В swap-файле записываем адрес (как int)
            Page page = GetPage(elementIndex);
            int pageOffset = (int)(elementIndex % ElementsPerPage);
            page.SetIntValue(pageOffset, address);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        // Чтение для varchar: считываем адрес из swap-файла, затем читаем строку из dataFileStream.
        private bool ReadVarcharElement(long elementIndex, out string value)
        {
            value = "";
            Page page = GetPage(elementIndex);
            int pageOffset = (int)(elementIndex % ElementsPerPage);
            if (!page.IsElementSet(pageOffset))
            {
                Console.WriteLine("Элемент не был записан (битовая карта = 0).");
                return false;
            }
            int address = page.GetIntValue(pageOffset);
            if (address == 0)
            {
                Console.WriteLine("Адрес равен нулю, строка не записана.");
                return false;
            }
            dataFileStream.Seek(address, SeekOrigin.Begin);
            byte[] lenBytes = new byte[4];
            int bytesRead = dataFileStream.Read(lenBytes, 0, 4);
            if (bytesRead < 4)
                return false;
            int len = BitConverter.ToInt32(lenBytes, 0);
            byte[] valueBytes = new byte[len];
            bytesRead = dataFileStream.Read(valueBytes, 0, len);
            if (bytesRead < len)
                return false;
            value = System.Text.Encoding.ASCII.GetString(valueBytes);
            return true;
        }

        // Закрытие файловых потоков
        public void Close()
        {
            foreach (var page in PageBuffer)
            {
                if (page.ModificationFlag)
                    SavePageToFile(page);
            }
            fileStream.Close();
            if (dataFileStream != null)
                dataFileStream.Close();
        }
    }

    // Класс, представляющий страницу в памяти
    public class Page
    {
        public int PageNumber { get; set; }
        public bool ModificationFlag { get; set; }
        public DateTime LastAccessTime { get; set; }
        public byte[] BitMap { get; set; } // Длина = ceil(ElementsPerPage/8)
        public byte[] Data { get; set; }     // Массив байт для хранения данных страницы

        public Page(int pageNumber, byte[] bitMap, byte[] data)
        {
            PageNumber = pageNumber;
            BitMap = bitMap;
            Data = data;
            ModificationFlag = false;
            LastAccessTime = DateTime.Now;
        }

        // Проверка: установлен ли бит для элемента с заданным offset
        public bool IsElementSet(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            if (byteIndex >= BitMap.Length)
                return false;
            return (BitMap[byteIndex] & (1 << bitIndex)) != 0;
        }

        // Установка бита для элемента с заданным offset
        public void SetElementBit(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            BitMap[byteIndex] |= (byte)(1 << bitIndex);
        }

        // Для int: чтение значения
        public int GetIntValue(int offset)
        {
            int dataOffset = BitMap.Length + offset * 4;
            return BitConverter.ToInt32(Data, dataOffset);
        }

        // Для int: запись значения
        public void SetIntValue(int offset, int value)
        {
            int dataOffset = BitMap.Length + offset * 4;
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, Data, dataOffset, 4);
            SetElementBit(offset);
        }

        // Для char: чтение строки фиксированной длины
        public string GetStringValue(int offset, int fixedLength)
        {
            int dataOffset = BitMap.Length + offset * fixedLength;
            byte[] strBytes = new byte[fixedLength];
            Array.Copy(Data, dataOffset, strBytes, 0, fixedLength);
            return System.Text.Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
        }

        // Для char: запись строки фиксированной длины
        public void SetStringValue(int offset, string value, int fixedLength)
        {
            int dataOffset = BitMap.Length + offset * fixedLength;
            byte[] strBytes = new byte[fixedLength];
            byte[] valueBytes = System.Text.Encoding.ASCII.GetBytes(value);
            int len = Math.Min(valueBytes.Length, fixedLength);
            Array.Copy(valueBytes, strBytes, len);
            Array.Copy(strBytes, 0, Data, dataOffset, fixedLength);
            SetElementBit(offset);
        }

        // Преобразование страницы в массив байт для записи в файл.
        public byte[] ToByteArray(int elementsPerPage, Type arrayType, int fixedLength, int pageSize)
        {
            byte[] pageBytes = new byte[pageSize];
            Array.Copy(BitMap, pageBytes, BitMap.Length);
            Array.Copy(Data, 0, pageBytes, BitMap.Length, Data.Length);
            return pageBytes;
        }

        // Создание страницы из массива байт, считанного из файла.
        public static Page FromByteArray(byte[] pageBytes, int pageNumber, int elementsPerPage, Type arrayType, int fixedLength, int pageSize)
        {
            int bitMapLength = (int)Math.Ceiling(elementsPerPage / 8.0);
            byte[] bitMap = new byte[bitMapLength];
            Array.Copy(pageBytes, 0, bitMap, 0, bitMapLength);
            int dataLength = pageSize - bitMapLength;
            byte[] data = new byte[dataLength];
            Array.Copy(pageBytes, bitMapLength, data, 0, dataLength);
            return new Page(pageNumber, bitMap, data);
        }
    }
}
