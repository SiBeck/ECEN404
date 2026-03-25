"""
example.py – Type-marshaling demos for Python.NET integration tests.
"""


def greet(name: str) -> str:
    return f"Hello, {name}!"


def add(a: int, b: int) -> int:
    return a + b


def multiply(a: float, b: float) -> float:
    return a * b


def reverse_string(s: str) -> str:
    return s[::-1]


def make_patient_record(patient_id: str, name: str, age: int) -> dict:
    return {
        "patient_id": patient_id,
        "name": name,
        "age": age,
        "status": "active",
    }


def fibonacci(n: int) -> list[int]:
    if n <= 0:
        return []
    seq = [0, 1]
    while len(seq) < n:
        seq.append(seq[-1] + seq[-2])
    return seq[:n]


def get_module_info() -> dict:
    return {
        "module_name": "example",
        "framework": "pythonnet",
    }
