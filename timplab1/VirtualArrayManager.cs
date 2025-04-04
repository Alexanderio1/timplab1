using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace timplab1
{
    public class VirtualArrayManager
    {
        public const int PAGE_SIZE = 512;              // Размер страницы в байтах
        private const int SIGNATURE_LENGTH = 2;         // Сигнатура – 2 байта ("ВМ")
        private readonly string FilePath;

        public long ArraySize { get; private set; }     // Общее количество элементов
        public Type ArrayType { get; private set; }       // Тип элементов (int, char, string)
        public int FixedStringLength { get; private set; } // Для char – длина строки; для varchar – макс. длина

        private int ElementsPerPage;                    // Количество элементов на страницу
        private int TotalPages;                         // Общее число страниц
        private FileStream fileStream;                  // Файловый поток для работы с файлом подкачки

        // Буфер страниц – хранит загруженные страницы
        private List<Page> PageBuffer;

        public VirtualArrayManager(string filePath, long arraySize, Type arrayType, int fixedStringLength = 0)
        {
            FilePath = filePath;
            ArraySize = arraySize;
            ArrayType = arrayType;
            FixedStringLength = fixedStringLength;
            PageBuffer = new List<Page>();

            // Определяем ElementsPerPage в зависимости от типа
            if (ArrayType == typeof(int))
            {
                // Подбираем число так, чтобы: (ElementsPerPage * 4 + битовая карта) = 512 байт.
                // Для ElementsPerPage = 124, битовая карта = ceil(124/8) = 16 байт, итого = 124*4 + 16 = 512.
                ElementsPerPage = 124;
            }
            else if (ArrayType == typeof(char))
            {
                // Согласно заданию: на странице 128 элементов
                ElementsPerPage = 128;
            }
            else if (ArrayType == typeof(string))
            {
                // Для varchar используем 128 элементов (адреса), данные строк будут храниться в отдельном файле (здесь – заготовка)
                ElementsPerPage = 128;
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

        private void InitializeFile()
        {
            bool fileExists = File.Exists(FilePath);
            fileStream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (!fileExists)
            {
                // Записываем сигнатуру (две байта, например "ВМ")
                byte[] signature = System.Text.Encoding.ASCII.GetBytes("ВМ");
                fileStream.Write(signature, 0, SIGNATURE_LENGTH);

                // Резервируем место под страницы: TotalPages * PAGE_SIZE байт
                fileStream.SetLength(SIGNATURE_LENGTH + TotalPages * PAGE_SIZE);

                // Можно заполнить страницы нулями (если требуется)
                // Для простоты здесь пропускаем заполнение.
            }
        }

        private void LoadInitialPages()
        {
            // Загружаем первые 3 страницы (или меньше, если TotalPages < 3)
            int pagesToLoad = Math.Min(3, TotalPages);
            for (int i = 0; i < pagesToLoad; i++)
            {
                Page page = LoadPageFromFile(i);
                PageBuffer.Add(page);
            }
        }

        // Метод для получения страницы из буфера или загрузки её из файла
        private Page GetPage(long elementIndex)
        {
            int absolutePageNumber = (int)(elementIndex / ElementsPerPage);
            // Пытаемся найти страницу в буфере
            Page page = PageBuffer.Find(p => p.PageNumber == absolutePageNumber);
            if (page != null)
            {
                // Обновляем время обращения
                page.LastAccessTime = DateTime.Now;
                return page;
            }

            // Если в буфере нет, выбираем самую старую страницу для замещения
            if (PageBuffer.Count >= 3)
            {
                Page oldestPage = PageBuffer[0];
                foreach (var p in PageBuffer)
                {
                    if (p.LastAccessTime < oldestPage.LastAccessTime)
                        oldestPage = p;
                }
                // Если страница модифицирована, сохраняем её
                if (oldestPage.ModificationFlag)
                {
                    SavePageToFile(oldestPage);
                }
                // Удаляем её из буфера
                PageBuffer.Remove(oldestPage);
            }
            // Загружаем новую страницу
            Page newPage = LoadPageFromFile(absolutePageNumber);
            PageBuffer.Add(newPage);
            return newPage;
        }

        // Сохранение страницы в файл
        private void SavePageToFile(Page page)
        {
            Console.WriteLine($"Сохраняем страницу {page.PageNumber} в файл.");
            long pagePosition = SIGNATURE_LENGTH + page.PageNumber * PAGE_SIZE;
            fileStream.Seek(pagePosition, SeekOrigin.Begin);
            byte[] pageBytes = page.ToByteArray(ElementsPerPage, ArrayType, FixedStringLength);
            fileStream.Write(pageBytes, 0, pageBytes.Length);
            fileStream.Flush();
            page.ModificationFlag = false;
        }

        // Загрузка страницы из файла
        private Page LoadPageFromFile(int pageNumber)
        {
            Console.WriteLine($"Загружаем страницу {pageNumber} из файла.");
            long pagePosition = SIGNATURE_LENGTH + pageNumber * PAGE_SIZE;
            fileStream.Seek(pagePosition, SeekOrigin.Begin);
            byte[] pageBytes = new byte[PAGE_SIZE];
            fileStream.Read(pageBytes, 0, PAGE_SIZE);
            Page page = Page.FromByteArray(pageBytes, pageNumber, ElementsPerPage, ArrayType, FixedStringLength);
            page.LastAccessTime = DateTime.Now;
            return page;
        }

        // Метод чтения для int
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

        // Метод записи для int
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

        // Метод чтения для char/fixed string и varchar (возвращает строку)
        public bool ReadElement(long elementIndex, out string value)
        {
            value = "";
            if (ArrayType != typeof(char) && ArrayType != typeof(string))
                throw new InvalidOperationException("Неверный тип операции чтения для данного массива.");

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

        // Метод записи для char/fixed string и varchar
        public bool WriteElement(long elementIndex, string value)
        {
            if (ArrayType != typeof(char) && ArrayType != typeof(string))
                throw new InvalidOperationException("Неверный тип операции записи для данного массива.");

            Page page = GetPage(elementIndex);
            int offset = (int)(elementIndex % ElementsPerPage);
            page.SetStringValue(offset, value, FixedStringLength);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        // Метод закрытия файлового потока и выгрузки буфера
        public void Close()
        {
            // Сохраняем все модифицированные страницы
            foreach (var page in PageBuffer)
            {
                if (page.ModificationFlag)
                    SavePageToFile(page);
            }
            fileStream.Close();
        }
    }

    // Структура (класс) страницы в памяти
    public class Page
    {
        public int PageNumber { get; set; }
        public bool ModificationFlag { get; set; }
        public DateTime LastAccessTime { get; set; }
        public byte[] BitMap { get; set; } // Длина = ceil(ElementsPerPage/8)
        // Для хранения данных: используем универсальное представление в виде массива байт.
        // При необходимости преобразуем в нужный тип (int или строку).
        public byte[] Data { get; set; }

        public Page(int pageNumber, byte[] bitMap, byte[] data)
        {
            PageNumber = pageNumber;
            BitMap = bitMap;
            Data = data;
            ModificationFlag = false;
            LastAccessTime = DateTime.Now;
        }

        // Проверяет, установлен ли бит для элемента с индексом offset
        public bool IsElementSet(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            if (byteIndex >= BitMap.Length) return false;
            return (BitMap[byteIndex] & (1 << bitIndex)) != 0;
        }

        // Устанавливает бит для элемента с индексом offset
        public void SetElementBit(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            BitMap[byteIndex] |= (byte)(1 << bitIndex);
        }

        // Методы для работы с данными типа int
        public int GetIntValue(int offset)
        {
            // Для int, данные хранятся начиная с позиции = (битовая карта) длиной (BitMap.Length)
            int dataOffset = BitMap.Length + offset * 4;
            return BitConverter.ToInt32(Data, dataOffset);
        }

        public void SetIntValue(int offset, int value)
        {
            int dataOffset = BitMap.Length + offset * 4;
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, Data, dataOffset, 4);
            SetElementBit(offset);
        }

        // Методы для работы со строковыми данными (char/fixed string и varchar)
        // В этом упрощенном примере каждая строка занимает FixedLength байт.
        public string GetStringValue(int offset, int fixedLength)
        {
            int dataOffset = BitMap.Length + offset * fixedLength;
            byte[] strBytes = new byte[fixedLength];
            Array.Copy(Data, dataOffset, strBytes, 0, fixedLength);
            // Удаляем нулевые символы
            return System.Text.Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
        }

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

        // Преобразование страницы в массив байт для сохранения в файл.
        public byte[] ToByteArray(int elementsPerPage, Type arrayType, int fixedLength)
        {
            // Для упрощения предполагаем, что длина Data равна PAGE_SIZE - (размер битовой карты)
            // Здесь Data уже сформирован нужного размера (PAGE_SIZE - BitMap.Length) + BitMap.Length = PAGE_SIZE.
            // Если Data меньше PAGE_SIZE, дополним нулями.
            byte[] pageBytes = new byte[VirtualArrayManager.PAGE_SIZE];
            // Сначала битовая карта
            Array.Copy(BitMap, pageBytes, BitMap.Length);
            // Затем данные
            Array.Copy(Data, 0, pageBytes, BitMap.Length, Data.Length);
            return pageBytes;
        }

        // Создание страницы из массива байт, считанного из файла.
        public static Page FromByteArray(byte[] pageBytes, int pageNumber, int elementsPerPage, Type arrayType, int fixedLength)
        {
            // Вычисляем длину битовой карты
            int bitMapLength = (int)Math.Ceiling(elementsPerPage / 8.0);
            byte[] bitMap = new byte[bitMapLength];
            Array.Copy(pageBytes, 0, bitMap, 0, bitMapLength);
            // Остальные байты – данные
            int dataLength = VirtualArrayManager.PAGE_SIZE - bitMapLength;
            byte[] data = new byte[dataLength];
            Array.Copy(pageBytes, bitMapLength, data, 0, dataLength);

            return new Page(pageNumber, bitMap, data);
        }
    }
}
