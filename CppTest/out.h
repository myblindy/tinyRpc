#pragma once

#include <Windows.h>
#include <vector>
#include <string>
#include <chrono>
#include <thread>
        
class TinyRpcServer
{
	HANDLE hPipe;
	std::unique_ptr<std::jthread> listenerThread;

	std::vector<uint8_t> incomingBuffer;

	void ReadFromPipe(size_t atLeastBytes)
	{
		constexpr auto readChunkSize = 100;
		char readChunk[readChunkSize] = {};

		while (incomingBuffer.size() < atLeastBytes)
		{
			// ensure at least readChunkSize reserved space
			if (incomingBuffer.capacity() < incomingBuffer.size() + readChunkSize)
				incomingBuffer.reserve(incomingBuffer.size() + readChunkSize);

			// read chunk
			DWORD readBytesCount = readChunkSize;
			auto result = ReadFile(hPipe, readChunk, readChunkSize, &readBytesCount, nullptr);

			// broken pipe?
			if (!result)
			{
				auto lastError = GetLastError();
				if (lastError == ERROR_PIPE_NOT_CONNECTED || lastError == ERROR_BROKEN_PIPE)
					exit(4);
			}

			// copy chunk into buffer
			incomingBuffer.insert(incomingBuffer.end(), readChunk, readChunk + readBytesCount);
		}
	}

	void ListenHandler()
	{
		// connect
		DWORD mode = PIPE_READMODE_BYTE | PIPE_WAIT;
		if (!SetNamedPipeHandleState(hPipe, nullptr, nullptr, nullptr))
			exit(3);

		while (!listenerThread->get_stop_token().stop_requested())
		{
			auto methodId = ReadNext<std::string>();

            if(methodId == "Hi")
{
    
    
    Hi();
    
}
if(methodId == "FancyHi")
{
    auto p0 = ReadNext<std::string>();
auto p1 = ReadNext<int32_t>();
    
    FancyHi(p0, p1);
    
}
if(methodId == "Add")
{
    auto p0 = ReadNext<int32_t>();
auto p1 = ReadNext<int32_t>();
    Write((uint8_t)0);     // data
auto result = 
    Add(p0, p1);
    Write(result);
}
if(methodId == "BufferCall")
{
    auto p0 = ReadNext<std::vector<uint8_t>>();
auto p1 = ReadNext<int32_t>();
    Write((uint8_t)0);     // data
auto result = 
    BufferCall(p0, p1);
    Write(result);
}
if(methodId == "GetValueTupleResult")
{
    auto p0 = ReadNext<std::string>();
    Write((uint8_t)0);     // data
auto result = 
    GetValueTupleResult(p0);
    Write(result);
}
if(methodId == "GetValueTupleArrayResult")
{
    
    Write((uint8_t)0);     // data
auto result = 
    GetValueTupleArrayResult();
    Write(result);
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
			for (auto i = 0; i < len; i++)
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
#pragma endregion
            
#pragma region Write Functions
	template<typename T>
	void Write(const T& value)
	{
		WriteFile(hPipe, &value, sizeof(T), nullptr, nullptr);
	}

	template<>
	void Write(const std::string& value)
	{
		auto len = static_cast<uint8_t>(value.size());
		Write(len);
		WriteFile(hPipe, value.data(), len, nullptr, nullptr);
	}

	template<typename TValue>
	void Write(const std::vector<TValue>& value)
	{
		Write(static_cast<uint32_t>(value.size()));
		for (const auto& item : value)
			Write(item);
	}
#pragma endregion

            
public:
	TinyRpcServer(int argc, char** argv)
	{
		if (argc < 1) exit(1);

		// open the pipe
		hPipe = CreateFileA((std::string("\\\\.\\pipe\\") + argv[1]).c_str(), GENERIC_READ | GENERIC_WRITE,
			0, nullptr, OPEN_EXISTING, 0, nullptr);
		if (hPipe == INVALID_HANDLE_VALUE) exit(2);

		// start the listener thread
		listenerThread = std::make_unique<std::jthread>(&TinyRpcServer::ListenHandler, this);
	}

	virtual ~TinyRpcServer()
	{
		listenerThread->request_stop();
		listenerThread->join();
		CloseHandle(hPipe);
	}
	                
    virtual void Hi() = 0;
virtual void FancyHi(std::string name, int32_t age) = 0;
virtual int32_t Add(int32_t x, int32_t y) = 0;
virtual std::vector<uint8_t> BufferCall(std::vector<uint8_t> baseUtf8String, int32_t n) = 0;
virtual std::tuple<int32_t, int32_t, int16_t, std::vector<uint8_t>> GetValueTupleResult(std::string s) = 0;
virtual std::vector<std::tuple<uint32_t, int64_t, std::chrono::system_clock::time_point, double>> GetValueTupleArrayResult() = 0;
};
