using Mono.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;
using TinyRpc.Roslyn;

string? inputProjectPath = null, outputPath = null;
string[]? inputClassNames = null, outputClassNames = null;
var optionSet = new OptionSet
{
    { "input-project-path=", p => inputProjectPath = p },
    { "input-class-names=", p => inputClassNames = p.Split(',') },
    { "output-path=", p => outputPath = p },
    { "output-class-names=", p => outputClassNames = p.Split(',') }
};
optionSet.Parse(args);

if (string.IsNullOrWhiteSpace(inputProjectPath) || inputClassNames is null || string.IsNullOrWhiteSpace(outputPath) || outputClassNames is null)
{
    Console.Error.WriteLine("Usage:");
    optionSet.WriteOptionDescriptions(Console.Error);
    return;
}

// load the project
MSBuildLocator.RegisterDefaults();
var project = await MSBuildWorkspace.Create().OpenProjectAsync(inputProjectPath);
//project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
if (await project.GetCompilationAsync() is not { } compilation) return;

// output stream
using var outputStream = new StreamWriter(outputPath);
outputStream.WriteLine("""
        #pragma once

        #include <Windows.h>
        #include <vector>
        #include <string>
        #include <chrono>
        #include <thread>
        
        #pragma warning( push )
        #pragma warning( disable : 6387)
        
        """);

// parse the compilation for symbols
foreach (var (inputClassName, outputClassName) in inputClassNames.Zip(outputClassNames))
    if (compilation.GetTypeByMetadataName(inputClassName) is { } classSymbol
        && compilation.GetSemanticModel(classSymbol.DeclaringSyntaxReferences[0].SyntaxTree) is { } semanticModel)
    {
        var events = classSymbol.GetMembers().OfType<IEventSymbol>().ToList();
        var methods = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => !events.Any(e => m.Name == $"add_{e.Name}" || m.Name == $"remove_{e.Name}"))
            .ToList();

        outputStream.WriteLine($$"""
            class {{outputClassName}}
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
            			auto methodId = ReadNext<std::string>();

                        {{string.Join("\n", methods.Select(m => $$"""
                            if(methodId == "{{m.Name}}")
                            {
                                {{string.Join("\n", m.Parameters.Select((p, pIdx) => $$"""
                                    auto p{{pIdx}} = ReadNext<{{CsToCppType(p.Type)}}>();
                                    """))}}
                                {{(m.ReturnsVoid ? null : "auto result = ")}}
                                {{m.Name}}({{string.Join(", ", Enumerable.Range(0, m.Parameters.Length).Select(i => $"p{i}"))}});
                                {{(m.ReturnsVoid ? null : $"""
                                    EnterCriticalSection(&writeCriticalSection);
                                    Write((uint8_t)0);     // data
                                    Write(result);
                                    FlushFileBuffers(hPipe);
                                    LeaveCriticalSection(&writeCriticalSection);
                                    """)}}
                            }
                            """))}}
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

            	template<typename... TValues>
            	void Write(const std::tuple<TValues...>& value)
            	{
            		std::apply([&](auto&&... args) { (Write(args), ...); }, value);
            	}

            	void Write(const std::chrono::system_clock::time_point& value)
            	{
            		// convert value to .NET ticks
            		auto duration = value.time_since_epoch();
            		long long ticks = std::chrono::duration_cast<std::chrono::nanoseconds>(duration).count() / 100
            			+ 621355968000000000;

            		Write(ticks);
            	}
            #pragma endregion
            
            public:
            	{{outputClassName}}(int argc, char** argv)
            	{
            		if (argc < 1) exit(1);

            		// open the pipe
            		hPipe = CreateFileA((std::string("\\\\.\\pipe\\") + argv[1]).c_str(), GENERIC_READ | GENERIC_WRITE,
            			0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
            		if (hPipe == INVALID_HANDLE_VALUE) exit(2);

            		// start the listener thread
                    InitializeCriticalSection(&writeCriticalSection);
            		listenerThread = std::make_unique<std::jthread>(&{{outputClassName}}::ListenHandler, this);
            	}

            	virtual ~{{outputClassName}}()
            	{
            		listenerThread->request_stop();
            		listenerThread->join();
            		CloseHandle(hPipe);
            	}

                {{string.Join("\n", events.Select(e => $$"""
                    void Fire{{e.Name}}({{string.Join(", ", (e.Type as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters
                        .Select(p => $"{CsToCppType(p.Type)} {p.Name}") ?? Array.Empty<string>())}})
                    {
                        EnterCriticalSection(&writeCriticalSection);
                        Write((uint8_t)1);     // event data
                        Write(std::string("{{e.Name}}"));
                        {{string.Join("\n", (e.Type as INamedTypeSymbol)?.DelegateInvokeMethod?.Parameters
                            .Select(p => $"Write({p.Name});") ?? Array.Empty<string>())}}
                        FlushFileBuffers(hPipe);
                        LeaveCriticalSection(&writeCriticalSection);
                    }
                    """))}}
            	                
                {{string.Join("\n", methods.Select(m => $$"""
                    virtual {{CsToCppType(m.ReturnType)}} {{m.Name}}({{string.Join(", ",
                        m.Parameters.Select(p => $"{CsToCppType(p.Type)} {p.Name}"))}}) = 0;
                    """))}}
            };

            #pragma warning( pop )
            """);
    }

static string CsToCppType(ITypeSymbol typeSymbol) => typeSymbol.ToFullyQualifiedString() switch
{
    _ when typeSymbol is IArrayTypeSymbol arrayTypeSymbol =>
        $"std::vector<{CsToCppType(arrayTypeSymbol.ElementType)}>",
    _ when typeSymbol.IsTupleType =>
        $"std::tuple<{string.Join(", ", ((INamedTypeSymbol)typeSymbol).TupleElements.Select(e => CsToCppType(e.Type)))}>",
    "global::System.Boolean" => "bool",
    "global::System.Byte" => "uint8_t",
    "global::System.SByte" => "int8_t",
    "global::System.Int16" => "int16_t",
    "global::System.UInt16" => "uint16_t",
    "global::System.Int32" => "int32_t",
    "global::System.UInt32" => "uint32_t",
    "global::System.Int64" => "int64_t",
    "global::System.UInt64" => "uint64_t",
    "global::System.Single" => "float",
    "global::System.Double" => "double",
    "global::System.String" => "std::string",
    "global::System.DateTime" => "std::chrono::system_clock::time_point",
    "global::System.Void" => "void",
    _ => throw new NotImplementedException($"Could not get C++ type for {typeSymbol.ToFullyQualifiedString()}")
};