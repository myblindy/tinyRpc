#pragma once

#include <Windows.h>
#include <vector>
#include <string>
#include <chrono>
#include <thread>

#pragma warning( push )
#pragma warning( disable : 6387)

// forward declares for supporting types
enum class E : int32_t;
struct S1;
struct S11;
struct S2;
struct S22;

// supporting types
enum class E : int32_t
{
    A = 0,
B = 1,
C = 2,
D = 3
};
struct S1
{
    int32_t a;
std::string b;
std::unique_ptr<S11> S11;
};
struct S11
{
    int32_t a;
};
struct S2
{
    std::string c;
uint64_t d;
std::unique_ptr<S22> S22;
};
struct S22
{
    int32_t a;
};


class TinyRpcServer
{
    HANDLE hPipe;
    CRITICAL_SECTION writeCriticalSection;
    std::unique_ptr<std::jthread> listenerThread;

    std::vector<uint8_t> incomingBuffer;

    void ReadFromPipe(size_t atLeastBytes)
    {
        constexpr auto readChunkSize = 100;
        char readChunk[readChunkSize] = {};

        OVERLAPPED ov{};
        ov.hEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

        while (incomingBuffer.size() < atLeastBytes)
        {
            // ensure at least readChunkSize reserved space
            if (incomingBuffer.capacity() < incomingBuffer.size() + readChunkSize)
                incomingBuffer.reserve(incomingBuffer.size() + readChunkSize);
    
            // read chunk
            DWORD readBytesCount = readChunkSize;
            auto result = ReadFile(hPipe, readChunk, readChunkSize, &readBytesCount, &ov);
    
            if (!result)
            {
                auto lastError = GetLastError();
    
                // broken pipe?
                if (lastError == ERROR_PIPE_NOT_CONNECTED || lastError == ERROR_BROKEN_PIPE)
                    exit(4);
                else if (lastError == ERROR_IO_PENDING)
                {
                    // wait for the operation to complete
                    WaitForSingleObject(ov.hEvent, INFINITE);
                    GetOverlappedResult(hPipe, &ov, &readBytesCount, TRUE);
                }
                else
                    exit(5);
            }
    
            // copy chunk into buffer
            incomingBuffer.insert(incomingBuffer.end(), readChunk, readChunk + readBytesCount);
        }

        CloseHandle(ov.hEvent);
    }

    void ListenHandler()
    {
        while (!listenerThread->get_stop_token().stop_requested())
        {
            auto methodIdx = ReadNext<uint8_t>();

            if(methodIdx == 0)       // "Add"
{
    auto p0 = ReadNext<int32_t>();
auto p1 = ReadNext<int32_t>();
    auto result = 
    Add(p0, p1);
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 1)       // "BufferCall"
{
    auto p0 = ReadNext<std::vector<uint8_t>>();
auto p1 = ReadNext<int32_t>();
    auto result = 
    BufferCall(p0, p1);
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 2)       // "FancyHi"
{
    auto p0 = ReadNext<std::string>();
auto p1 = ReadNext<int32_t>();
    
    FancyHi(p0, p1);
    
}
if(methodIdx == 3)       // "GetNewE"
{
    auto p0 = ReadNext<E>();
    auto result = 
    GetNewE(p0);
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 4)       // "GetStruct"
{
    auto p0 = ReadNext<int32_t>();
auto p1 = ReadNext<std::unique_ptr<S1>>();
auto p2 = ReadNext<double>();
    auto result = 
    GetStruct(p0, std::move(p1), p2);
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 5)       // "GetValueTupleArrayResult"
{
    
    auto result = 
    GetValueTupleArrayResult();
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 6)       // "GetValueTupleResult"
{
    auto p0 = ReadNext<std::string>();
    auto result = 
    GetValueTupleResult(p0);
    EnterCriticalSection(&writeCriticalSection);
Write((uint8_t)0);     // data
Write(result);
FlushFileBuffers(hPipe);
LeaveCriticalSection(&writeCriticalSection);
}
if(methodIdx == 7)       // "Hi"
{
    
    
    Hi();
    
}
        }
    }

#pragma region Read Functions
    template<typename T> struct is_std_vector final : std::false_type {};
    template<typename... T> struct is_std_vector<std::vector<T...>> final : std::true_type {};

    template<typename T>
    T ReadNext()
    {
        if constexpr (is_std_vector<T>::value)
        {
            auto len = ReadNext<uint32_t>();
        
            T result;
            result.reserve(len);
            for (size_t i = 0; i < len; i++)
                result.push_back(ReadNext<typename T::value_type>());
            return result;
        }
        else
        {
            ReadFromPipe(sizeof(T));
            auto result = *reinterpret_cast<T*>(incomingBuffer.data());
            incomingBuffer.erase(incomingBuffer.begin(), incomingBuffer.begin() + sizeof(T));
            return result;
        }
    }

    template<>
    std::string ReadNext<std::string>()
    {
        auto len = ReadNext<uint8_t>();

        ReadFromPipe(len);
        auto result = std::string(reinterpret_cast<const char*>(incomingBuffer.data()), len);
        incomingBuffer.erase(incomingBuffer.begin(), incomingBuffer.begin() + len);
        return result;
    }

    template<>
    std::chrono::system_clock::time_point ReadNext<std::chrono::system_clock::time_point>()
    {
        auto ticks = ReadNext<int64_t>();
        std::chrono::nanoseconds ns{ (ticks - 621355968000000000) * 100 };

        // Construct a system_clock::time_point by adding the duration to the epoch
        return std::chrono::system_clock::from_time_t(0) + duration_cast<std::chrono::system_clock::duration>(ns);
    }

    
// read S1 struct
template<>
std::unique_ptr<S1> ReadNext<std::unique_ptr<S1>>()
{
    auto result = std::make_unique<S1>();
    result->a = ReadNext<int32_t>();
result->b = ReadNext<std::string>();
result->S11 = ReadNext<std::unique_ptr<S11>>();
    return result;
}
// read S11 struct
template<>
std::unique_ptr<S11> ReadNext<std::unique_ptr<S11>>()
{
    auto result = std::make_unique<S11>();
    result->a = ReadNext<int32_t>();
    return result;
}
#pragma endregion

#pragma region Write Functions
    template<typename T>
    void Write(const T& value) const
    {
        WriteFile(hPipe, &value, sizeof(T), nullptr, nullptr);
    }

    template<>
    void Write(const std::string& value) const
    {
        auto len = static_cast<uint8_t>(value.size());
        Write(len);
        WriteFile(hPipe, value.data(), len, nullptr, nullptr);
    }

    template<typename TValue>
    void Write(const std::vector<TValue>& value) const
    {
        Write(static_cast<uint32_t>(value.size()));
        for (const auto& item : value)
            Write(item);
    }

    template<typename... TValues>
    void Write(const std::tuple<TValues...>& value) const
    {
        std::apply([&](auto&&... args) { (Write(args), ...); }, value);
    }

    void Write(const std::chrono::system_clock::time_point& value) const
    {
        // convert value to .NET ticks
        auto duration = value.time_since_epoch();
        long long ticks = std::chrono::duration_cast<std::chrono::nanoseconds>(duration).count() / 100
            + 621355968000000000;

        Write(ticks);
    }

    
// write S1 struct
std::unique_ptr<S1> Write(const std::unique_ptr<S1>& value) const
{
    Write(value->a);
Write(value->b);
Write(value->S11);
}
// write S11 struct
std::unique_ptr<S11> Write(const std::unique_ptr<S11>& value) const
{
    Write(value->a);
}
    #pragma endregion

public:
    TinyRpcServer(int argc, char** argv)
    {
        if (argc < 1) exit(1);

        // open the pipe
        hPipe = CreateFileA((std::string("\\\\.\\pipe\\") + argv[1]).c_str(), GENERIC_READ | GENERIC_WRITE,
            0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (hPipe == INVALID_HANDLE_VALUE) exit(2);

        // start the listener thread
        InitializeCriticalSection(&writeCriticalSection);
        listenerThread = std::make_unique<std::jthread>(&TinyRpcServer::ListenHandler, this);
    }

    virtual ~TinyRpcServer()
    {
        listenerThread->request_stop();
        listenerThread->join();
        CloseHandle(hPipe);
    }

    void FireOnData(double arg1, std::string arg2)
{
    EnterCriticalSection(&writeCriticalSection);
    Write((uint8_t)1);     // event data
    Write((uint8_t)0);     //OnData
    Write(arg1);
Write(arg2);
    FlushFileBuffers(hPipe);
    LeaveCriticalSection(&writeCriticalSection);
}

    virtual int32_t Add(int32_t x, int32_t y) = 0;
virtual std::vector<uint8_t> BufferCall(std::vector<uint8_t> baseUtf8String, int32_t n) = 0;
virtual void FancyHi(std::string name, int32_t age) = 0;
virtual E GetNewE(E input) = 0;
virtual S2 GetStruct(int32_t a, std::unique_ptr<S1> s, double b) = 0;
virtual std::vector<std::tuple<uint32_t, int64_t, std::chrono::system_clock::time_point, double>> GetValueTupleArrayResult() = 0;
virtual std::tuple<int32_t, int32_t, int16_t, std::vector<uint8_t>> GetValueTupleResult(std::string s) = 0;
virtual void Hi() = 0;
};

#pragma warning( pop )
