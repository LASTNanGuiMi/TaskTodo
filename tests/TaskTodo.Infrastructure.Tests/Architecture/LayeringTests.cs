using TaskTodo.Core.Interaction;
using TaskTodo.Infrastructure.Storage;

namespace TaskTodo.Infrastructure.Tests.Architecture;

public sealed class LayeringTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceWpfOrInfrastructure()
    {
        var referenceNames = typeof(WindowPresentationController)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("PresentationFramework", referenceNames);
        Assert.DoesNotContain("PresentationCore", referenceNames);
        Assert.DoesNotContain("TaskTodo.Infrastructure", referenceNames);
    }

    [Fact]
    public void InfrastructureAssemblyDoesNotReferenceWpfOrApp()
    {
        var referenceNames = typeof(SqliteTaskTodoRepository)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("PresentationFramework", referenceNames);
        Assert.DoesNotContain("PresentationCore", referenceNames);
        Assert.DoesNotContain("TaskTodo", referenceNames);
    }
}
