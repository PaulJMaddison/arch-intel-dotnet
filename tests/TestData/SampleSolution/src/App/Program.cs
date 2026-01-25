using SampleSolution.Core;
using SampleSolution.Data;

var repository = new Repository(new Clock());
Console.WriteLine(repository.GetTimestamp());
