using System;
using System.Collections.Generic;
using System.Text;

namespace UserApi.Models.Dtos
{
    internal class UserDto
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }

        public static UserDto? ToDto(User user)
        {
            if (user == null)
                return null;

            var dto = new UserDto();
            
            dto.Id = user.Id;
            dto.FirstName = user.FirstName;
            dto.LastName = user.LastName;
            dto.Email = user.Email;

            return dto;

        }
    }
}
