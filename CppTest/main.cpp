#include "TinyRpcServer.h"

#include <iostream>
#include <regex>

using namespace std::chrono_literals;

#undef min
#undef max

class ServerHandler : public TinyRpcServer
{
	std::vector<uint8_t> to_vector(const std::string& str) { return { str.begin(), str.end() }; }

public:
	ServerHandler(int argc, char** argv) : TinyRpcServer(argc, argv) { }

	void Hi() override { std::cout << "Hi" << std::endl; }
	void FancyHi(std::string name, int32_t age) override { std::cout << "Fancy hi, " << age << " years old " << name << std::endl; }
	int32_t Add(int32_t x, int32_t y) override { return x + y; }
	std::vector<uint8_t> BufferCall(std::vector<uint8_t> baseUtf8String, int32_t n) override
	{
		std::stringstream ss;
		ss << std::string(baseUtf8String.begin(), baseUtf8String.end()) << " x" << n;

		auto str = ss.str();
		return { str.begin(), str.end() };
	}
	std::tuple<int32_t, int32_t, int16_t, std::vector<uint8_t>> GetValueTupleResult(std::string s) override
	{
		std::regex regex("([0-9]+) ([0-9]+) ([0-9]+) (.*)");
		std::smatch match;
		std::regex_match(s, match, regex);
		if (match.size() != 5) throw std::runtime_error("Invalid string format");
		return std::make_tuple(std::stoi(match[1]), std::stoi(match[2]), std::stoi(match[3]), to_vector(match[4].str()));
	}
	std::vector<std::tuple<uint32_t, int64_t, std::chrono::system_clock::time_point, double>> GetValueTupleArrayResult() override
	{
		return std::vector<std::tuple<uint32_t, int64_t, std::chrono::system_clock::time_point, double>>
		{
			std::make_tuple(1, 2, std::chrono::system_clock::now(), 3.14),
			std::make_tuple(4, 5, std::chrono::system_clock::now() - 10s, 6.28),
			std::make_tuple(7, 8, std::chrono::system_clock::now() + 10s, 9.42),
		};
	}
	E GetNewE(E input)
	{
		return (E)((int)input + 1 % 4);
	}
};

int main(int argc, char* argv[])
{
	ServerHandler server(argc, argv);

	while (true)
	{
		Sleep(800);
		server.FireOnData(rand() / (double)RAND_MAX, "moopsies");
	}
}
