"""
Example module for BioMetrix Python.NET integration testing.

Provides simple functions to verify that basic Python data types
(strings, ints, floats, dicts, lists) marshal correctly across
the Python.NET boundary.
"""


def greet(name):
    """Return a greeting string."""
    return f"Hello, {name}!"


def add(a, b):
    """Return the sum of two numbers."""
    return a + b


def multiply(a, b):
    """Return the product of two numbers."""
    return a * b


def reverse_string(s):
    """Return the reversed string."""
    return s[::-1]


def make_patient_record(patient_id, name, age):
    """Return a dict representing a patient record."""
    return {
        "patient_id": patient_id,
        "name": name,
        "age": age,
        "status": "active",
    }


def fibonacci(n):
    """Return a list of the first n Fibonacci numbers."""
    if n <= 0:
        return []
    if n == 1:
        return [0]
    seq = [0, 1]
    for _ in range(2, n):
        seq.append(seq[-1] + seq[-2])
    return seq


def get_module_info():
    """Return metadata about this module."""
    return {
        "module_name": "example",
        "framework": "pythonnet",
    }
