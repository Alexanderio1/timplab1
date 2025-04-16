using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace timplab1
{
    public class VirtualArrayManager
    {
        private const int SIGNATURE_LENGTH = 2; // "VM"
        private const int BITMAP_SIZE = 16;

        // Для int / varchar-адресов
        private const int INT_DATA_SIZE = 512; // 128 * 4
        private const int INT_PAGE_SIZE = BITMAP_SIZE + INT_DATA_SIZE; // 528

        private const int ELEMENTS_PER_PAGE = 128;

        // ------ Поля класса ------
        private readonly string filePath;   // Основной файл (pagefile)
        private FileStream fileStream;

        // Только для varchar - отдельный .dat
        private string dataFilePath;
        private FileStream dataFileStream;

        public long ArraySize { get; private set; }        // Кол-во элементов
        public Type ArrayType { get; private set; }         // int / char / string(varchar)
        public int FixedStringLength { get; private set; }  // для char(...) / varchar(...)

        // Доступны на чтение извне (например, для команды info)
        public string FilePath => filePath;
        public string DataFilePath => dataFilePath;

        private int dataSize;   // байт под «элементы» страницы
        private int pageSize;   // итого байт на страницу (dataSize + BITMAP_SIZE)
        private int totalPages;

        private List<Page> pageBuffer; // буфер страниц (не менее 3)

        // --------------------------------------------------------------------------------
        //                        Конструктор
        // --------------------------------------------------------------------------------

        public VirtualArrayManager(string filePath, long arraySize, Type arrayType, int fixedStringLength = 0)
        {
            this.filePath = filePath;
            ArraySize = arraySize;
            ArrayType = arrayType;
            FixedStringLength = fixedStringLength;

            pageBuffer = new List<Page>();

            if (arrayType == typeof(int))
            {
                // int = 128 * 4 (512) + 16 = 528
                dataSize = INT_DATA_SIZE;
                pageSize = INT_PAGE_SIZE;
            }
            else if (arrayType == typeof(char))
            {
                // char(L) → 128 * L → округлить до кратного 512 + 16
                int rawSize = ELEMENTS_PER_PAGE * FixedStringLength;
                dataSize = RoundUpToMultipleOf512(rawSize);
                pageSize = BITMAP_SIZE + dataSize;
            }
            else if (arrayType == typeof(string))
            {
                // varchar → 128 * 4 + 16 = 528, плюс отдельный файл .dat
                dataSize = INT_DATA_SIZE; // 512
                pageSize = INT_PAGE_SIZE; // 528

                dataFilePath = filePath + ".dat";
            }
            else
            {
                throw new ArgumentException("Неподдерживаемый тип массива (int|char|varchar).");
            }

            // Считаем количество страниц
            totalPages = (int)Math.Ceiling(ArraySize / (double)ELEMENTS_PER_PAGE);

            InitializeMainFile();
            if (ArrayType == typeof(string))
            {
                InitializeDataFile();
            }

            LoadInitialPages();
        }

        // --------------------------------------------------------------------------------
        //                       Инициализация
        // --------------------------------------------------------------------------------

        private int RoundUpToMultipleOf512(int value)
        {
            if (value <= 512)
                return 512;
            int remainder = value % 512;
            if (remainder == 0)
                return value;
            return value + (512 - remainder);
        }

        private void InitializeMainFile()
        {
            bool fileExists = File.Exists(filePath);
            fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

            if (!fileExists)
            {
                // Пишем сигнатуру "VM"
                byte[] sig = Encoding.ASCII.GetBytes("VM");
                fileStream.Write(sig, 0, SIGNATURE_LENGTH);

                long totalSize = SIGNATURE_LENGTH + (long)totalPages * pageSize;
                fileStream.SetLength(totalSize);
                fileStream.Flush();
            }
        }

        private void InitializeDataFile()
        {
            // Файл для хранения фактических данных varchar
            bool fileExists = File.Exists(dataFilePath);
            dataFileStream = new FileStream(dataFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (!fileExists)
            {
                // Можно записать сигнатуру "VM2" - не обязательно
            }
        }

        private void LoadInitialPages()
        {
            int pagesToLoad = Math.Min(3, totalPages);
            for (int i = 0; i < pagesToLoad; i++)
            {
                Page page = LoadPageFromFile(i);
                pageBuffer.Add(page);
            }
        }

        // --------------------------------------------------------------------------------
        //                 Чтение / запись страниц
        // --------------------------------------------------------------------------------

        private Page LoadPageFromFile(int pageNumber)
        {
            long offset = SIGNATURE_LENGTH + (long)pageNumber * pageSize;
            fileStream.Seek(offset, SeekOrigin.Begin);

            byte[] pageBytes = new byte[pageSize];
            fileStream.Read(pageBytes, 0, pageSize);

            byte[] bitMap = new byte[BITMAP_SIZE];
            Array.Copy(pageBytes, 0, bitMap, 0, BITMAP_SIZE);

            byte[] data = new byte[dataSize];
            Array.Copy(pageBytes, BITMAP_SIZE, data, 0, dataSize);

            var page = new Page(pageNumber, bitMap, data);
            return page;
        }

        private void SavePageToFile(Page page)
        {
            long offset = SIGNATURE_LENGTH + (long)page.PageNumber * pageSize;
            fileStream.Seek(offset, SeekOrigin.Begin);

            byte[] pageBytes = new byte[pageSize];
            Array.Copy(page.BitMap, 0, pageBytes, 0, BITMAP_SIZE);
            Array.Copy(page.Data, 0, pageBytes, BITMAP_SIZE, dataSize);

            fileStream.Write(pageBytes, 0, pageBytes.Length);
            fileStream.Flush();

            page.ModificationFlag = false;
        }

        // --------------------------------------------------------------------------------
        //                 Буфер (минимум 3 страницы)
        // --------------------------------------------------------------------------------

        private Page GetPage(long elementIndex)
        {
            if (elementIndex < 0 || elementIndex >= ArraySize)
                throw new IndexOutOfRangeException($"Индекс {elementIndex} вне диапазона 0..{ArraySize - 1}");

            int pageNumber = (int)(elementIndex / ELEMENTS_PER_PAGE);

            Page page = pageBuffer.Find(p => p.PageNumber == pageNumber);
            if (page != null)
            {
                page.LastAccessTime = DateTime.Now;
                return page;
            }

            if (pageBuffer.Count >= 3)
            {
                // Ищем самую старую
                Page oldest = pageBuffer[0];
                foreach (var p in pageBuffer)
                {
                    if (p.LastAccessTime < oldest.LastAccessTime)
                        oldest = p;
                }
                if (oldest.ModificationFlag)
                    SavePageToFile(oldest);
                pageBuffer.Remove(oldest);
            }

            Page newPage = LoadPageFromFile(pageNumber);
            newPage.LastAccessTime = DateTime.Now;
            pageBuffer.Add(newPage);
            return newPage;
        }

        public void Close()
        {
            foreach (var p in pageBuffer)
            {
                if (p.ModificationFlag)
                    SavePageToFile(p);
            }
            fileStream?.Close();
            dataFileStream?.Close();
        }

        // --------------------------------------------------------------------------------
        //                      Методы для int
        // --------------------------------------------------------------------------------

        public bool WriteElement(long index, int value)
        {
            if (ArrayType != typeof(int))
                throw new InvalidOperationException("Тип массива не int.");

            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            page.SetIntValue(offset, value);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        public bool ReadElement(long index, out int value)
        {
            value = 0;
            if (ArrayType != typeof(int))
                throw new InvalidOperationException("Тип массива не int.");

            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            if (!page.IsElementSet(offset))
                return false;

            value = page.GetIntValue(offset);
            return true;
        }

        // --------------------------------------------------------------------------------
        //                      Методы для char(fixed)
        // --------------------------------------------------------------------------------

        public bool WriteElement(long index, string strValue)
        {
            if (ArrayType != typeof(char))
                throw new InvalidOperationException("Тип массива не char(...).");

            // Обрезаем по макс. длине
            if (strValue.Length > FixedStringLength)
                strValue = strValue.Substring(0, FixedStringLength);

            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            page.SetStringValue(offset, strValue, FixedStringLength);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        public bool ReadElement(long index, out string strValue)
        {
            strValue = "";
            if (ArrayType != typeof(char))
                throw new InvalidOperationException("Тип массива не char(...).");

            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            if (!page.IsElementSet(offset))
                return false;

            strValue = page.GetStringValue(offset, FixedStringLength);
            return true;
        }

        // --------------------------------------------------------------------------------
        //                       Методы для varchar
        // --------------------------------------------------------------------------------

        /// <summary>
        /// Записывает строку переменной длины, но не более FixedStringLength (maxLength).
        /// 1) Если строка длиннее maxLength, обрезаем.
        /// 2) Записываем (length + bytes) в .dat,
        /// 3) В основной файл пишем 4-байтовый offset.
        /// </summary>
        public bool WriteElementVarchar(long index, string fullStr)
        {
            if (ArrayType != typeof(string))
                throw new InvalidOperationException("Тип массива не varchar.");

            if (dataFileStream == null)
                throw new Exception("dataFileStream не инициализирован (null).");

            // Применяем ограничение maxLength
            if (fullStr.Length > FixedStringLength)
            {
                fullStr = fullStr.Substring(0, FixedStringLength);
            }

            // 1) Уходим в конец dataFile (доп. файл)
            long offsetInData = dataFileStream.Seek(0, SeekOrigin.End);
            if (offsetInData > int.MaxValue)
                throw new Exception("Смещение превысило int.MaxValue");

            // 2) Пишем 4 байта длины
            int strLen = fullStr.Length;
            byte[] lenBytes = BitConverter.GetBytes(strLen);
            dataFileStream.Write(lenBytes, 0, 4);

            // 3) Пишем сами байты строки
            byte[] strBytes = Encoding.UTF8.GetBytes(fullStr);
            dataFileStream.Write(strBytes, 0, strBytes.Length);
            dataFileStream.Flush();

            // 4) Полученное смещение (int) пишем в основной файл
            int offsetInt = (int)offsetInData;
            return WriteElementAsInt(index, offsetInt);
        }

        /// <summary>
        /// Читает строку из .dat:
        /// 1) Читаем offset (4 байта) из основной страницы,
        /// 2) Идём в dataFileStream → first 4 байта = длина, далее столько байт строки.
        /// </summary>
        public bool ReadElementVarchar(long index, out string result)
        {
            result = "";
            if (ArrayType != typeof(string))
                throw new InvalidOperationException("Тип массива не varchar.");

            if (!ReadElementAsInt(index, out int offset))
            {
                return false; // бит не установлен
            }
            if (offset < 0)
            {
                return false;
            }

            dataFileStream.Seek(offset, SeekOrigin.Begin);

            // Читаем 4 байта длины
            byte[] lenBytes = new byte[4];
            dataFileStream.Read(lenBytes, 0, 4);
            int strLen = BitConverter.ToInt32(lenBytes, 0);
            if (strLen <= 0)
            {
                result = "";
                return true;
            }

            // Читаем сами байты
            byte[] strBytes = new byte[strLen];
            dataFileStream.Read(strBytes, 0, strLen);

            result = Encoding.UTF8.GetString(strBytes);
            return true;
        }

        // Вспомогательные методы для хранения offset (int) в «int-структуре» страницы.
        private bool WriteElementAsInt(long index, int intValue)
        {
            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            page.SetIntValue(offset, intValue);
            page.ModificationFlag = true;
            page.LastAccessTime = DateTime.Now;
            return true;
        }

        private bool ReadElementAsInt(long index, out int intValue)
        {
            intValue = 0;
            Page page = GetPage(index);
            int offset = (int)(index % ELEMENTS_PER_PAGE);
            if (!page.IsElementSet(offset))
                return false;

            intValue = page.GetIntValue(offset);
            return true;
        }
    }

    // =============================================================================
    // Класс Page: хранит 16 байт битовой карты (на 128 элементов) + data (512 или
    // выровненное число байт), где элемент может быть int или часть строки
    // =============================================================================
    public class Page
    {
        public int PageNumber { get; set; }
        public bool ModificationFlag { get; set; }
        public DateTime LastAccessTime { get; set; }

        public byte[] BitMap { get; private set; }
        public byte[] Data { get; private set; }

        public Page(int pageNumber, byte[] bitMap, byte[] data)
        {
            PageNumber = pageNumber;
            BitMap = bitMap;
            Data = data;
            ModificationFlag = false;
            LastAccessTime = DateTime.Now;
        }

        // ----------------- Работа с битовой картой -----------------
        public bool IsElementSet(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            if (byteIndex >= BitMap.Length)
                return false;
            return (BitMap[byteIndex] & (1 << bitIndex)) != 0;
        }

        private void SetBit(int offset)
        {
            int byteIndex = offset / 8;
            int bitIndex = offset % 8;
            BitMap[byteIndex] |= (byte)(1 << bitIndex);
        }

        // ----------------- Методы для int/varchar (4 байта) -----------------
        public int GetIntValue(int offset)
        {
            int dataOffset = offset * 4;
            return BitConverter.ToInt32(Data, dataOffset);
        }

        public void SetIntValue(int offset, int value)
        {
            int dataOffset = offset * 4;
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, Data, dataOffset, 4);
            SetBit(offset);
        }

        // ----------------- Методы для char(fixed) -----------------
        public string GetStringValue(int offset, int fixedLen)
        {
            int dataOffset = offset * fixedLen;
            if (dataOffset + fixedLen > Data.Length)
                return "";

            byte[] strBytes = new byte[fixedLen];
            Array.Copy(Data, dataOffset, strBytes, 0, fixedLen);

            // Удаляем завершающие \0
            return Encoding.ASCII.GetString(strBytes).TrimEnd('\0');
        }

        public void SetStringValue(int offset, string value, int fixedLen)
        {
            int dataOffset = offset * fixedLen;
            byte[] buffer = new byte[fixedLen];
            byte[] valBytes = Encoding.ASCII.GetBytes(value);

            int len = Math.Min(valBytes.Length, fixedLen);
            Array.Copy(valBytes, buffer, len);

            Array.Copy(buffer, 0, Data, dataOffset, fixedLen);
            SetBit(offset);
        }
    }
}
