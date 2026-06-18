using TurisClick.Api.Modules.Users.DTOs;

namespace TurisClick.Api.Modules.Users.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserResponse>> GetAllAsync();
    Task<UserResponse> GetByIdAsync(int userId);
    Task<UserResponse> CreateAsync(CreateUserRequest request);
    Task<UserResponse> UpdateAsync(int userId, UpdateUserRequest request);
    Task<UserResponse> ChangeStatusAsync(int userId, ChangeUserStatusRequest request);
    Task<UserResponse> ChangeRoleAsync(int userId, ChangeUserRoleRequest request);
}
