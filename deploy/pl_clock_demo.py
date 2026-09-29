from pynq import Clocks

print("before:", Clocks.fclk0_mhz, "MHz")
Clocks.fclk0_mhz = 100
print("after:", Clocks.fclk0_mhz, "MHz")