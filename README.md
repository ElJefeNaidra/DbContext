# DbContext
A lightweight MSSQL/MySQL Stored Procedure based DBContext library

A small library written in C# enabling developers flexible CRUD implementations.

Features
- Supports both MSSQL and MYSQL
- Uses reflection
- Execute Stored Procedures
- Parameter sniffing and maping
- Mapping of model object properties to stored procedure parameters
- Handling of datatype binding
- Caching of SP params and Model Properties
- It can map multiple models a single sotred procedure by using prioritisation and deep model scanning
- It can serialize data to JSON for List<T> to map to 
