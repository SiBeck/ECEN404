"""
Example Python module for Python.NET integration testing.

Provides simple functions that demonstrate basic data type marshaling
between Python and C# via the pythonnet bridge.

Usage from C#:
    dynamic example = Py.Import("example");
    string greeting = example.greet("World");
    int result = example.add(2, 3);
"""


def greet(name: str) -> str:
    """Return a greeting string. Demonstrates string marshaling."""
    return f"Hello, {name}!"


def add(a: int, b: int) -> int:
    """Return the sum of two integers. Demonstrates numeric marshaling."""
    return a + b


def multiply(a: float, b: float) -> float:
    """Return the product of two floats. Demonstrates float marshaling."""
    return a * b


def reverse_string(text: str) -> str:
    """Return the reversed string. Demonstrates string round-tripping."""
    return text[::-1]


def make_patient_record(patient_id: str, name: str, age: int) -> dict:
    """
    Build a patient record dictionary.

    Demonstrates dictionary marshaling — C# receives this as a PyDict
    which can be accessed via indexing or converted to a .NET dictionary.

    Args:
        patient_id: Unique patient identifier.
        name: Patient full name.
        age: Patient age in years.

    Returns:
        Dictionary with patient metadata.
    """
    return {
        "patient_id": patient_id,
        "name": name,
        "age": age,
        "status": "active",
    }


def fibonacci(n: int) -> list:
    """
    Return the first n Fibonacci numbers as a list.

    Demonstrates list marshaling — C# receives this as a PyList.

    Args:
        n: Number of Fibonacci values to generate (must be >= 0).

    Returns:
        List of integers.
    """
    if n <= 0:
        return []
    if n == 1:
        return [0]
    seq = [0, 1]
    for _ in range(2, n):
        seq.append(seq[-1] + seq[-2])
    return seq


def get_module_info() -> dict:
    """
    Return metadata about this module.

    Useful for C# integration tests to verify the module loaded correctly.
    """
    return {
        "module_name": "example",
        "version": "1.0.0",
        "description": "Example module for Python.NET integration testing",
        "framework": "pythonnet",
    }


if __name__ == "__main__":
    print("=== Example Module Self-Test ===")
    print(f"greet: {greet('BioMetrix')}")
    print(f"add: {add(10, 20)}")
    print(f"multiply: {multiply(3.14, 2.0)}")
    print(f"reverse: {reverse_string('DICOM')}")
    print(f"patient: {make_patient_record('PAT001', 'Test Patient', 45)}")
    print(f"fibonacci(8): {fibonacci(8)}")
    print(f"module info: {get_module_info()}")
