package com.endava.juniorprogram.usersprogram.services;

import com.endava.juniorprogram.usersprogram.model.User;

import java.util.List;

public interface UsersService {
    List<User> getUsers(String name);
    User getUser(long id);
    User deleteUser(long id);
    User addUser(User user);
    User updateUser(User user);
}
