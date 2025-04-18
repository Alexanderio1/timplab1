using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace timplab1
{
    /// <summary>
    ///  Управление «виртуальным» массивом‑файлом: int, char(…),
    ///  varchar(…) (с доп.‑файлом).  Буфер = 3 страницы, алгоритм LRU,
    ///  в консоль выводятся события подкачки страниц.
    /// </summary>
    public class VirtualArrayManager
    {
        private const int SIGNATURE_LENGTH = 2;        // "VM"
        private const int BITMAP_SIZE = 16;       // 128 бит

        // int / varchar‑offset (4 байта на элемент)
        private const int INT_DATA_SIZE = 512;         // 128 × 4
        private const int INT_PAGE_SIZE = BITMAP_SIZE + INT_DATA_SIZE; // 528

        private const int ELEMENTS_PER_PAGE = 128;     // на каждой странице

        // ---------- файлы ----------
        private readonly string filePath;              // основной .bin
        private FileStream fileStream;

        private string dataFilePath;                   // для varchar
        private FileStream dataFileStream;

        // ---------- параметры массива ----------
        public long ArraySize { get; }
        public Type ArrayType { get; }
        public int FixedStringLength { get; }       // char / varchar
        public string FilePath => filePath;
        public string DataFilePath => dataFilePath;

        // ---------- расчётные величины ----------
        private readonly int dataSize;                 // байт данных в странице
        private readonly int pageSize;                 // + BITMAP_SIZE
        private readonly int totalPages;

        // ---------- буфер страниц ----------
        private readonly List<Page> pageBuffer = new();     // <= 3 страниц

        // =========================================================================
        //                          КОНСТРУКТОР
        // =========================================================================
        public VirtualArrayManager(string filePath,
                                   long arraySize,
                                   Type arrayType,
                                   int fixedStringLength = 0)
        {
            this.filePath = filePath;
            ArraySize = arraySize;
            ArrayType = arrayType;
            FixedStringLength = fixedStringLength;

            if (arrayType == typeof(int) || arrayType == typeof(string))
            {
                dataSize = INT_DATA_SIZE;          // 512
                pageSize = INT_PAGE_SIZE;          // 528
                if (arrayType == typeof(string))
                    dataFilePath = filePath + ".dat";
            }
            else if (arrayType == typeof(char))
            {
                int raw = ELEMENTS_PER_PAGE * FixedStringLength;
                dataSize = RoundUpTo512(raw);
                pageSize = BITMAP_SIZE + dataSize;
            }
            else
                throw new ArgumentException("Поддерживаются только int|char|varchar");

            totalPages = (int)Math.Ceiling(arraySize / (double)ELEMENTS_PER_PAGE);

            InitMainFile();
            if (ArrayType == typeof(string)) InitDataFile();

            LoadInitialPages();
        }

        // -------------------------------------------------------------------------
        //  ФАЙЛОВАЯ ИНИЦИАЛИЗАЦИЯ
        // -------------------------------------------------------------------------
        private static int RoundUpTo512(int v) => v % 512 == 0 ? v : v + 512 - v % 512;

        private void InitMainFile()
        {
            bool exists = File.Exists(filePath);
            fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

            if (!exists)
            {
                fileStream.Write(Encoding.ASCII.GetBytes("VM"), 0, SIGNATURE_LENGTH);
                fileStream.SetLength(SIGNATURE_LENGTH + (long)totalPages * pageSize);
                fileStream.Flush();
            }
        }

        private void InitDataFile()
        {
            dataFileStream = new FileStream(dataFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        }

        // -------------------------------------------------------------------------
        //  ПЕРВЫЕ 3 СТРАНИЦЫ В БУФЕР
        // -------------------------------------------------------------------------
        private void LoadInitialPages()
        {
            int n = Math.Min(3, totalPages);
            for (int i = 0; i < n; i++)
                pageBuffer.Add(LoadPageFromDisk(i));
        }

        // -------------------------------------------------------------------------
        //                ЗАГРУЗКА / ВЫГРУЗКА СТРАНИЦ
        // -------------------------------------------------------------------------
        private Page LoadPageFromDisk(int pageNo)
        {
            long ofs = SIGNATURE_LENGTH + (long)pageNo * pageSize;
            fileStream.Seek(ofs, SeekOrigin.Begin);

            byte[] buf = new byte[pageSize];
            fileStream.Read(buf);

            byte[] map = new byte[BITMAP_SIZE];
            byte[] data = new byte[dataSize];
            Array.Copy(buf, 0, map, 0, BITMAP_SIZE);
            Array.Copy(buf, BITMAP_SIZE, data, 0, dataSize);

            Console.WriteLine($"[SWAP‑IN ] <- страница {pageNo}");
            return new Page(pageNo, map, data);
        }

        private void SavePageToDisk(Page pg)
        {
            long ofs = SIGNATURE_LENGTH + (long)pg.PageNumber * pageSize;
            fileStream.Seek(ofs, SeekOrigin.Begin);

            byte[] buf = new byte[pageSize];
            Array.Copy(pg.BitMap, buf, BITMAP_SIZE);
            Array.Copy(pg.Data, 0, buf, BITMAP_SIZE, dataSize);

            fileStream.Write(buf);
            fileStream.Flush();
            pg.ModificationFlag = false;

            Console.WriteLine($"[SWAP‑OUT] -> страница {pg.PageNumber}");
        }

        // -----------------------------------------------------------------------
        //        ПОЛУЧИТЬ СТРАНИЦУ  (буфер 3 шт., вытеснение LRU)
        // -----------------------------------------------------------------------
        private Page GetPage(long elementIndex)
        {
            // --- контроль диапазона --------------------------------------------------
            if (elementIndex < 0 || elementIndex >= ArraySize)
                throw new IndexOutOfRangeException(
                    $"Индекс {elementIndex} вне диапазона 0..{ArraySize - 1}");

            int pageNo = (int)(elementIndex / ELEMENTS_PER_PAGE);

            // --- страница уже в памяти? ---------------------------------------------
            Page pg = pageBuffer.Find(p => p.PageNumber == pageNo);
            if (pg != null)
            {
                pg.LastAccessTime = DateTime.Now;
                return pg;
            }

            // --- буфер полон -> вытесняем старейшую (LRU) ----------------------------
            if (pageBuffer.Count >= 3)
            {
                Page oldest = pageBuffer[0];
                foreach (var p in pageBuffer)
                    if (p.LastAccessTime < oldest.LastAccessTime) oldest = p;

                if (oldest.ModificationFlag) SavePageToDisk(oldest);
                Console.WriteLine($"[SWAP‑OUT] -> страница {oldest.PageNumber}");
                pageBuffer.Remove(oldest);
            }

            // --- подкачиваем нужную страницу -----------------------------------------
            pg = LoadPageFromDisk(pageNo);          // в LoadPage… уже есть [SWAP‑IN]‑лог
            pg.LastAccessTime = DateTime.Now;
            pageBuffer.Add(pg);
            return pg;
        }


        // -------------------------------------------------------------------------
        //  PUBLIC: ЗАКРЫТЬ ФАЙЛЫ
        // -------------------------------------------------------------------------
        public void Close()
        {
            foreach (var p in pageBuffer)
                if (p.ModificationFlag) SavePageToDisk(p);

            fileStream?.Close();
            dataFileStream?.Close();
        }

        // =========================================================================
        //                           INT
        // =========================================================================
        public bool WriteElement(long index, int value)
        {
            if (ArrayType != typeof(int)) throw new InvalidOperationException();
            Page pg = GetPage(index);
            pg.SetIntValue((int)(index % ELEMENTS_PER_PAGE), value);
            pg.ModificationFlag = true;
            return true;
        }

        public bool ReadElement(long index, out int value)
        {
            value = 0;
            if (ArrayType != typeof(int)) throw new InvalidOperationException();
            Page pg = GetPage(index);
            int off = (int)(index % ELEMENTS_PER_PAGE);
            if (!pg.IsElementSet(off)) return false;
            value = pg.GetIntValue(off);
            return true;
        }

        // =========================================================================
        //                           CHAR(FIXED)
        // =========================================================================
        public bool WriteElement(long index, string val)
        {
            if (ArrayType != typeof(char)) throw new InvalidOperationException();
            if (val.Length > FixedStringLength) val = val[..FixedStringLength];

            Page pg = GetPage(index);
            pg.SetStringValue((int)(index % ELEMENTS_PER_PAGE), val, FixedStringLength);
            pg.ModificationFlag = true;
            return true;
        }

        public bool ReadElement(long index, out string val)
        {
            val = "";
            if (ArrayType != typeof(char)) throw new InvalidOperationException();
            Page pg = GetPage(index);
            int off = (int)(index % ELEMENTS_PER_PAGE);
            if (!pg.IsElementSet(off)) return false;
            val = pg.GetStringValue(off, FixedStringLength);
            return true;
        }

        // =========================================================================
        //                            VARCHAR
        // =========================================================================
        public bool WriteElementVarchar(long index, string text)
        {
            if (ArrayType != typeof(string)) throw new InvalidOperationException();
            if (text.Length > FixedStringLength) text = text[..FixedStringLength];

            long offset = dataFileStream.Seek(0, SeekOrigin.End);
            if (offset > int.MaxValue)
                throw new Exception("Offset > Int32");

            // запись в .dat  (4 байта length + bytes)
            dataFileStream.Write(BitConverter.GetBytes(text.Length));
            dataFileStream.Write(Encoding.UTF8.GetBytes(text));
            dataFileStream.Flush();

            return WriteOffset(index, (int)offset);
        }

        public bool ReadElementVarchar(long index, out string text)
        {
            text = "";
            if (ArrayType != typeof(string)) throw new InvalidOperationException();
            if (!ReadOffset(index, out int offset)) return false;

            dataFileStream.Seek(offset, SeekOrigin.Begin);
            Span<byte> lenBuf = stackalloc byte[4];
            dataFileStream.Read(lenBuf);
            int len = BitConverter.ToInt32(lenBuf);

            byte[] bytes = new byte[len];
            dataFileStream.Read(bytes);
            text = Encoding.UTF8.GetString(bytes);
            return true;
        }

        // offset as INT inside normal page
        private bool WriteOffset(long index, int off)
        {
            Page pg = GetPage(index);
            pg.SetIntValue((int)(index % ELEMENTS_PER_PAGE), off);
            pg.ModificationFlag = true;
            return true;
        }

        private bool ReadOffset(long index, out int off)
        {
            off = 0;
            Page pg = GetPage(index);
            int pos = (int)(index % ELEMENTS_PER_PAGE);
            if (!pg.IsElementSet(pos)) return false;
            off = pg.GetIntValue(pos);
            return true;
        }
    }

    // =============================================================================
    //                                PAGE
    // =============================================================================
    internal class Page
    {
        public int PageNumber { get; }
        public byte[] BitMap { get; }
        public byte[] Data { get; }

        public bool ModificationFlag { get; set; }
        public DateTime LastAccessTime { get; set; }

        public Page(int no, byte[] map, byte[] data)
        {
            PageNumber = no;
            BitMap = map;
            Data = data;
            LastAccessTime = DateTime.Now;
        }

        // --- bitmap ---
        public bool IsElementSet(int idx) =>
            (BitMap[idx / 8] & (1 << (idx % 8))) != 0;

        private void SetBit(int idx) =>
            BitMap[idx / 8] |= (byte)(1 << (idx % 8));

        // --- int / offset (4 байта) ---
        public int GetIntValue(int idx) =>
            BitConverter.ToInt32(Data, idx * 4);

        public void SetIntValue(int idx, int val)
        {
            Array.Copy(BitConverter.GetBytes(val), 0, Data, idx * 4, 4);
            SetBit(idx);
        }

        // --- fixed string ---
        public string GetStringValue(int idx, int len)
        {
            byte[] tmp = new byte[len];
            Array.Copy(Data, idx * len, tmp, 0, len);
            return Encoding.ASCII.GetString(tmp).TrimEnd('\0');
        }

        public void SetStringValue(int idx, string s, int len)
        {
            byte[] buf = new byte[len];
            byte[] src = Encoding.ASCII.GetBytes(s);
            Array.Copy(src, buf, Math.Min(src.Length, len));

            Array.Copy(buf, 0, Data, idx * len, len);
            SetBit(idx);
        }
    }
}
